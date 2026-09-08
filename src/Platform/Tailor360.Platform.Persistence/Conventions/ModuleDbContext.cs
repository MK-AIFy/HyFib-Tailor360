using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Conventions;

/// <summary>
/// The base every module's database context derives from. It fixes the conventions once — schema
/// isolation, snake_case naming, UTC timestamps, decimal money, application-assigned keys and
/// <c>xmin</c> concurrency — so that a module cannot drift from them by omission, and so that a
/// reviewer reading a module's configuration sees only what is genuinely specific to that module.
/// </summary>
/// <param name="options">Context options.</param>
/// <param name="schema">The schema this module owns. No other module may map a table in it (ARCH-005).</param>
public abstract class ModuleDbContext(DbContextOptions options, string schema) : DbContext(options)
{
    /// <summary>The schema this context owns.</summary>
    public string Schema { get; } = !string.IsNullOrWhiteSpace(schema)
        ? schema
        : throw new ArgumentException("A module context must declare its schema.", nameof(schema));

    /// <summary>The migration history table name used within the module's own schema.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// The module's outbox. Every integration event this module publishes is written here, in this
    /// module's own schema, by the same unit of work that writes the change it describes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mapped on the base rather than by each module for the reason the rest of this class exists: a
    /// module cannot then forget it, and a module added next year has it without anybody remembering.
    /// It is also the whole point of the table being here. A shared outbox on another context is a
    /// second connection and a second transaction, so publishing beside a module's own write could
    /// only ever be two commits — either committing work whose event is lost, or announcing work that
    /// rolled back (<see href="https://github.com/MK-AIFy/HyFib-Tailor360/issues/77">#77</see>).
    /// </para>
    /// <para>
    /// <see href="../../../docs/adr/0008-transactional-outbox-and-workers.md">ADR-0008</see> decided
    /// this shape and said why in the same paragraph: the transaction and the connection are not
    /// guaranteed across our per-module contexts.
    /// </para>
    /// </remarks>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// The module's inbox: one row per (message, handler) this module has already acted on.
    /// </summary>
    /// <remarks>
    /// Here rather than shared for the same reason, read from the consuming end. Delivery is
    /// at-least-once, and the row is what turns that into an at-most-once <em>effect</em> — but only if
    /// it commits with the effect it records, which means it has to be in the schema the handler writes
    /// to (ADR-0008 section 4.2).
    /// </remarks>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
        ConfigureOutbox(modelBuilder);
        ApplyConventions(modelBuilder);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Money is decimal everywhere. Declaring the facets centrally means a new column cannot quietly
        // land as numeric with no precision, which PostgreSQL would accept and which would then store
        // amounts no two environments agree on.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        configurationBuilder.Properties<string>().AreUnicode();

        // Applied as a finalizing convention rather than a loop in OnModelCreating, because a derived
        // context configures its entities after calling into this base and a loop here would run
        // before them. Finalizing runs once, over the completed model, so it cannot miss a type.
        configurationBuilder.Conventions.Add(_ => new ApplicationAssignedKeyConvention());

        base.ConfigureConventions(configurationBuilder);
    }

    /// <summary>
    /// Maps the module's outbox and inbox. The shape is the platform's, because the dispatcher reads
    /// every module's table with one statement and a schema that differed would need its own.
    /// </summary>
    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.Property(e => e.LeaseOwner).HasMaxLength(128);
            entity.Property(e => e.LastError).HasMaxLength(2000);

            // The dispatcher's claim query filters on unprocessed messages that are due; a partial
            // index keeps that query on a small index even once millions of delivered rows have
            // accumulated.
            entity.HasIndex(e => new { e.AvailableAt, e.AggregateId })
                .HasDatabaseName("ix_outbox_messages_pending")
                .HasFilter("processed_at IS NULL AND dead_lettered_at IS NULL");

            entity.HasIndex(e => e.ProcessedAt).HasDatabaseName("ix_outbox_messages_processed_at");
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(e => new { e.MessageId, e.HandlerName });
            entity.Property(e => e.HandlerName).HasMaxLength(200);
        });
    }

    private static void ApplyConventions(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                // Every instant is stored with its offset. A bare timestamp would be ambiguous the
                // moment a second branch in another timezone is added.
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetColumnType("timestamptz");
                }

                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetColumnType("timestamptz");
                }
            }
        }
    }

    /// <summary>
    /// Configures the optimistic-concurrency token for an entity. PostgreSQL's system column
    /// <c>xmin</c> is used rather than a hand-maintained version column, so concurrency is enforced even
    /// for a row changed by a migration or by an operator statement.
    /// </summary>
    protected static void UseRowVersion<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}

/// <summary>
/// Declares every <see cref="Guid"/> primary key as assigned by the application rather than generated
/// by the store.
/// </summary>
/// <remarks>
/// <para>
/// This is a correctness fix, not a tidying-up. Every identifier in this system is a UUIDv7 minted by
/// <c>IIdGenerator</c> before the entity exists (ARCH-015), so a key is never empty and never absent —
/// and EF's default for a <see cref="Guid"/> key is <c>ValueGeneratedOnAdd</c>, which makes change
/// detection read a non-empty key as "this row is already in the database".
/// </para>
/// <para>
/// The failure that produces is specific and easy to miss. Saving a brand-new aggregate works, because
/// the root is explicitly added and the whole graph goes with it. Attaching a child to an aggregate that
/// was <em>loaded</em> — enrolling an authenticator, printing a sheet of recovery codes, registering a
/// passkey, remembering a device — issues an UPDATE against a row that does not exist, which surfaces as
/// a concurrency exception on the second save rather than as anything naming the real cause.
/// </para>
/// <para>
/// It lives on the shared base because it is a consequence of a rule every module obeys. Left in one
/// module's context, the next module to attach a child to a loaded aggregate reproduces the defect and
/// spends the same afternoon finding it. Nothing in any schema changes: these columns never had a
/// database default.
/// </para>
/// </remarks>
public sealed class ApplicationAssignedKeyConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entity.FindPrimaryKey() is not { } key)
            {
                continue;
            }

            foreach (var property in key.Properties)
            {
                if (property.ClrType == typeof(Guid))
                {
                    property.Builder.ValueGenerated(ValueGenerated.Never);
                }
            }
        }
    }
}
