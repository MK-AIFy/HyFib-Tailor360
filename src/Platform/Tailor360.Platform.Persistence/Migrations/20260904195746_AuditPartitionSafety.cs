using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Platform.Persistence.Migrations
{
    /// <summary>
    /// Makes the audit trail safe against running out of partitions.
    ///
    /// The original design called the partition-creating function from the row-level BEFORE INSERT
    /// trigger and described that as a backstop. It is not one: PostgreSQL routes a row to its
    /// partition before any row-level trigger fires, so an insert whose month has no partition fails
    /// during routing and the trigger never runs. Once the pre-created months ran out, every audited
    /// mutation in the system would start failing — and because the audit entry is written in the same
    /// transaction as the change, that means every state change would fail.
    ///
    /// A DEFAULT partition removes the failure mode outright: a row for an unprovisioned month lands
    /// there instead of being rejected. Maintenance then keeps months provisioned ahead, and a health
    /// check reports when the default partition is not empty, which is the signal that maintenance has
    /// stopped running.
    /// </summary>
    public partial class AuditPartitionSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS platform.audit_events_default
                    PARTITION OF platform.audit_events DEFAULT;
                """);

            // The PERFORM is removed: it could never help the row being inserted, and leaving it in
            // place would keep implying a guarantee that does not exist.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION platform.audit_events_chain()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    head_hash     varchar(64);
                    head_sequence bigint;
                    material      text;
                BEGIN
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

            // Creating a month partition while the default partition holds rows for that month is
            // rejected by PostgreSQL. That only happens when maintenance has not run for a month, and
            // the default error message does not say what to do about it, so it is replaced with one
            // that does.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION platform.ensure_audit_partition(target timestamptz)
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

                    IF EXISTS (
                        SELECT 1 FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'platform' AND c.relname = partition
                    ) THEN
                        RETURN;
                    END IF;

                    BEGIN
                        EXECUTE format(
                            'CREATE TABLE platform.%I PARTITION OF platform.audit_events
                             FOR VALUES FROM (%L) TO (%L)',
                            partition, period_start, period_end);
                    EXCEPTION WHEN check_violation THEN
                        RAISE EXCEPTION
                            'Cannot create audit partition % because platform.audit_events_default '
                            'already holds rows for that month. Partition maintenance has not run. '
                            'Move those rows out of the default partition, then retry.', partition
                            USING HINT = 'See docs/platform/database.md, audit partition maintenance.';
                    END;
                END;
                $$;
                """);

            // How many entries are sitting in the default partition. Zero is the healthy state; any
            // other number means a month went unprovisioned and the rows have to be relocated before
            // that month can get a partition of its own.
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.audit_default_partition_rows()
                RETURNS bigint
                LANGUAGE sql
                STABLE
                AS $$ SELECT count(*) FROM platform.audit_events_default $$;
                """);

            migrationBuilder.Sql("SELECT platform.ensure_audit_partition(now() + interval '2 month');");
            migrationBuilder.Sql("SELECT platform.ensure_audit_partition(now() + interval '3 month');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform.audit_default_partition_rows();");
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform.audit_events_default;");
        }
    }
}
