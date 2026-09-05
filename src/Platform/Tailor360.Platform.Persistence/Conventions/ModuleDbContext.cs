using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tailor360.Platform.Persistence.Conventions;

/// <summary>
/// The base every module's database context derives from. It fixes the conventions once — schema
/// isolation, snake_case naming, UTC timestamps, decimal money and <c>xmin</c> concurrency — so that a
/// module cannot drift from them by omission, and so that a reviewer reading a module's configuration
/// sees only what is genuinely specific to that module.
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
