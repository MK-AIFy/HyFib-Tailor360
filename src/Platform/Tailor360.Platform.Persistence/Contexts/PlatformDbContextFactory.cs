using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Platform.Persistence.Contexts;

/// <summary>
/// Creates the context for the EF Core design-time tools. It never reads a deployed configuration: the
/// tools only need a provider and a model, so the connection string is a local placeholder and cannot
/// cause a design-time command to touch a real database by accident.
/// </summary>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    /// <summary>The placeholder used when generating migrations.</summary>
    public const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=tailor360_design_time;Username=design_time";

    /// <inheritdoc />
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("TAILOR360_DESIGN_TIME_DATABASE_URL")
            ?? DesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PlatformDbContext(options);
    }
}
