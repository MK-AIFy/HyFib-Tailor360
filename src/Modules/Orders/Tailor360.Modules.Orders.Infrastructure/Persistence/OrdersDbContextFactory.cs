using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>Design-time context for future Orders migrations.</summary>
public sealed class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("TAILOR360_DESIGN_TIME_DATABASE_URL")
            ?? "Host=localhost;Database=tailor360_design_time;Username=design_time";
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, OrdersDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new OrdersDbContext(options);
    }
}
