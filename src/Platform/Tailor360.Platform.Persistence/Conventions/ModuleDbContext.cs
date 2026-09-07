using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

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

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
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
