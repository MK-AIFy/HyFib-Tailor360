using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>
/// Builds the context for <c>dotnet ef</c>, which has no host to ask.
/// </summary>
/// <remarks>
/// The connection string is a placeholder that resolves to nothing, so a design-time command cannot
/// reach a real database by accident. <c>TAILOR360_DESIGN_TIME_DATABASE_URL</c> overrides it for the
/// one case that needs a live server — scaffolding from an existing schema.
/// </remarks>
public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    /// <summary>The placeholder a design-time command uses when nothing overrides it.</summary>
    public const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=tailor360_design_time;Username=design_time";

    /// <inheritdoc />
    public BillingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("TAILOR360_DESIGN_TIME_DATABASE_URL")
            ?? DesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, BillingDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new BillingDbContext(options);
    }
}
