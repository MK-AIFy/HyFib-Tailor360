using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Platform.Persistence.Migrations
{
    /// <summary>
    /// Creates the audit trail: a month-partitioned, append-only table whose rows are hash-chained by a
    /// database trigger. Writing this by hand rather than scaffolding it keeps the guarantees explicit —
    /// the chain, the immutability triggers and the verification function are the point of the table,
    /// and none of them can be expressed in the entity model.
    /// </summary>
    public partial class AuditTrailStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The chain head. Holding the current hash in one lockable row is what makes the chain
            // total: a writer takes the row lock, reads the previous hash and appends, so two
            // concurrent writers cannot both believe they follow the same entry.
            migrationBuilder.Sql("""
                CREATE TABLE platform.audit_chain_head (
                    id            smallint    PRIMARY KEY DEFAULT 1 CHECK (id = 1),
                    last_hash     varchar(64) NOT NULL,
                    last_sequence bigint      NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO platform.audit_chain_head (id, last_hash, last_sequence)
                VALUES (1, repeat('0', 64), 0);
                """);

            migrationBuilder.Sql("""
                CREATE TABLE platform.audit_events (
                    id                 uuid          NOT NULL,
                    occurred_at        timestamptz   NOT NULL,
                    sequence           bigint        NOT NULL,
                    actor_id           uuid          NULL,
                    actor_display_name varchar(200)  NOT NULL,
                    action             varchar(200)  NOT NULL,
                    entity_type        varchar(200)  NOT NULL,
                    entity_id          uuid          NOT NULL,
                    branch_id          uuid          NULL,
                    correlation_id     varchar(64)   NULL,
                    reason             varchar(1000) NULL,
                    summary            varchar(2000) NOT NULL,
                    before             jsonb         NULL,
                    after              jsonb         NULL,
                    previous_hash      varchar(64)   NOT NULL,
                    hash               varchar(64)   NOT NULL,
                    CONSTRAINT pk_audit_events PRIMARY KEY (id, occurred_at)
                ) PARTITION BY RANGE (occurred_at);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_audit_events_entity      ON platform.audit_events (entity_type, entity_id);
                CREATE INDEX ix_audit_events_occurred_at ON platform.audit_events (occurred_at);
                CREATE INDEX ix_audit_events_actor       ON platform.audit_events (actor_id);
                CREATE INDEX ix_audit_events_sequence    ON platform.audit_events (sequence);
                """);

            // Creating a partition on demand keeps a missing partition from turning into a failed write
            // at midnight on the first of a month. The worker calls this ahead of time; the trigger
            // calls it as a backstop.
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.ensure_audit_partition(target timestamptz)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    period_start date;
                    period_end   date;
                    partition    text;
                BEGIN
                    period_start := date_trunc('month', target AT TIME ZONE 'UTC')::date;
                    period_end   := (period_start + interval '1 month')::date;
                    partition    := format('audit_events_%s', to_char(period_start, 'YYYY_MM'));

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'platform' AND c.relname = partition
                    ) THEN
                        EXECUTE format(
                            'CREATE TABLE platform.%I PARTITION OF platform.audit_events
                             FOR VALUES FROM (%L) TO (%L)',
                            partition, period_start, period_end);
                    END IF;
                END;
                $$;
                """);

            // The chain itself. The hash covers the previous hash and every field that carries meaning,
            // so changing any of them after the fact fails verification for this row and for all later
            // rows, which is what makes tampering detectable rather than merely discouraged.
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.audit_events_chain()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    head_hash     varchar(64);
                    head_sequence bigint;
                    material      text;
                BEGIN
                    PERFORM platform.ensure_audit_partition(NEW.occurred_at);

                    SELECT last_hash, last_sequence
                      INTO head_hash, head_sequence
                      FROM platform.audit_chain_head
                     WHERE id = 1
                       FOR UPDATE;

                    material := concat_ws('|',
                        head_hash,
                        head_sequence + 1,
                        NEW.id::text,
                        to_char(NEW.occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                        coalesce(NEW.actor_id::text, ''),
                        NEW.actor_display_name,
                        NEW.action,
                        NEW.entity_type,
                        NEW.entity_id::text,
                        coalesce(NEW.branch_id::text, ''),
                        coalesce(NEW.correlation_id, ''),
                        coalesce(NEW.reason, ''),
                        NEW.summary,
                        coalesce(NEW.before::text, ''),
                        coalesce(NEW.after::text, ''));

                    NEW.sequence      := head_sequence + 1;
                    NEW.previous_hash := head_hash;
                    NEW.hash          := encode(sha256(convert_to(material, 'UTF8')), 'hex');

                    UPDATE platform.audit_chain_head
                       SET last_hash = NEW.hash, last_sequence = NEW.sequence
                     WHERE id = 1;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER audit_events_chain
                BEFORE INSERT ON platform.audit_events
                FOR EACH ROW EXECUTE FUNCTION platform.audit_events_chain();
                """);

            // Append-only. Revoking UPDATE and DELETE from the application role is the first line of
            // defence; this trigger is the second, so a role misconfiguration does not silently make
            // the trail editable.
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.audit_events_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'platform.audit_events is append-only; % is not permitted', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER audit_events_no_update
                BEFORE UPDATE OR DELETE ON platform.audit_events
                FOR EACH ROW EXECUTE FUNCTION platform.audit_events_append_only();
                """);

            // Verification recomputes the chain and reports the first entry that does not follow from
            // its predecessor. A superuser can disable a trigger; they cannot make the arithmetic agree.
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.verify_audit_chain(from_sequence bigint DEFAULT 0)
                RETURNS TABLE (broken_sequence bigint, broken_id uuid, reason text)
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    expected_previous varchar(64);
                    expected_sequence bigint;
                    entry             record;
                    material          text;
                    computed          varchar(64);
                BEGIN
                    IF from_sequence <= 0 THEN
                        expected_previous := repeat('0', 64);
                        expected_sequence := 1;
                    ELSE
                        SELECT hash INTO expected_previous
                          FROM platform.audit_events WHERE sequence = from_sequence;
                        expected_sequence := from_sequence + 1;
                    END IF;

                    FOR entry IN
                        SELECT * FROM platform.audit_events
                         WHERE sequence >= expected_sequence
                         ORDER BY sequence
                    LOOP
                        IF entry.sequence <> expected_sequence THEN
                            RETURN QUERY SELECT entry.sequence, entry.id,
                                format('expected sequence %s', expected_sequence)::text;
                            RETURN;
                        END IF;

                        IF entry.previous_hash <> expected_previous THEN
                            RETURN QUERY SELECT entry.sequence, entry.id, 'previous hash mismatch'::text;
                            RETURN;
                        END IF;

                        material := concat_ws('|',
                            entry.previous_hash,
                            entry.sequence,
                            entry.id::text,
                            to_char(entry.occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                            coalesce(entry.actor_id::text, ''),
                            entry.actor_display_name,
                            entry.action,
                            entry.entity_type,
                            entry.entity_id::text,
                            coalesce(entry.branch_id::text, ''),
                            coalesce(entry.correlation_id, ''),
                            coalesce(entry.reason, ''),
                            entry.summary,
                            coalesce(entry.before::text, ''),
                            coalesce(entry.after::text, ''));

                        computed := encode(sha256(convert_to(material, 'UTF8')), 'hex');

                        IF computed <> entry.hash THEN
                            RETURN QUERY SELECT entry.sequence, entry.id, 'content hash mismatch'::text;
                            RETURN;
                        END IF;

                        expected_previous := entry.hash;
                        expected_sequence := entry.sequence + 1;
                    END LOOP;
                END;
                $$;
                """);

            // The current month and the next one exist from the moment the schema does, so the first
            // write never races partition creation.
            migrationBuilder.Sql("""
                SELECT platform.ensure_audit_partition(now());
                SELECT platform.ensure_audit_partition(now() + interval '1 month');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_events_no_update ON platform.audit_events;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_events_chain ON platform.audit_events;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform.audit_events;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform.verify_audit_chain(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform.audit_events_append_only();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform.audit_events_chain();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform.ensure_audit_partition(timestamptz);");
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform.audit_chain_head;");
        }
    }
}
