using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Persistence.Conventions;

/// <summary>Database connection and pooling configuration.</summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// The connection string. Supplied as a secret file in every deployed environment, never in an
    /// environment variable, so it does not appear in a process listing.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The maximum size of this process's connection pool. The sum across every process must stay under
    /// the server's <c>max_connections</c> with headroom for administrative sessions and for pg_dump;
    /// the budget is checked at startup rather than discovered during a busy Saturday.
    /// </summary>
    [Range(1, 500)]
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>The server's configured maximum connections, used to validate the budget.</summary>
    [Range(1, 10000)]
    public int ServerMaxConnections { get; set; } = 100;

    /// <summary>
    /// How many connections are reserved for operators, backups and monitoring, and therefore excluded
    /// from the application's budget.
    /// </summary>
    [Range(0, 100)]
    public int ReservedConnections { get; set; } = 20;

    /// <summary>How many application processes share the server.</summary>
    [Range(1, 100)]
    public int ExpectedProcessCount { get; set; } = 2;

    /// <summary>Command timeout in seconds.</summary>
    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>The total pooled connections every process together may open.</summary>
    public int TotalBudget => MaxPoolSize * ExpectedProcessCount;

    /// <summary>The connections available to the application after the operator reserve.</summary>
    public int AvailableForApplication => ServerMaxConnections - ReservedConnections;

    /// <summary>True when the configured pools fit inside the server's capacity.</summary>
    public bool BudgetFits => TotalBudget <= AvailableForApplication;
}
