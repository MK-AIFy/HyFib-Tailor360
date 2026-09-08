using System.ComponentModel.DataAnnotations;
using Tailor360.Modules.Customers.Domain.Customers;

namespace Tailor360.Modules.Customers.Application.Options;

/// <summary>
/// How a generated subject-access export behaves: how long the download works for, and how much the
/// cleanup job takes in one pass.
/// </summary>
public sealed class CustomerExportOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Customers:Export";

    /// <summary>
    /// How long the download works for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is the <b>proposed</b> seven days in
    /// <c>docs/nfr/data-classification.md</c> section 9, and it is proposed rather than settled because
    /// it belongs to <b>OD-08</b> in <c>docs/prd/assumptions-and-open-decisions.md</c>, which is the
    /// business owner's to answer. It is a setting rather than a constant so that answering OD-08 is a
    /// configuration change and not a release.
    /// </para>
    /// <para>
    /// The domain clamps whatever is configured to
    /// <see cref="CustomerExport.MinimumLifetime"/>..<see cref="CustomerExport.MaximumLifetime"/>, so a
    /// deployment cannot leave a copy of somebody's personal data downloadable for a year by editing a
    /// file.
    /// </para>
    /// </remarks>
    [Range(typeof(TimeSpan), "00:15:00", "30.00:00:00")]
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How often the cleanup job looks for exports to empty.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "24:00:00")]
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The most exports one cleanup pass empties, so a backlog cannot turn one run into an unbounded
    /// transaction.
    /// </summary>
    [Range(1, 10_000)]
    public int PurgeBatchSize { get; set; } = 200;

    /// <summary>True when the configured lifetime is one the domain will accept unchanged.</summary>
    public bool IsLifetimeUsable
        => Lifetime >= CustomerExport.MinimumLifetime && Lifetime <= CustomerExport.MaximumLifetime;
}
