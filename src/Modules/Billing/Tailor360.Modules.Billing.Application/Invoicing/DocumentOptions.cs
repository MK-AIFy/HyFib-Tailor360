using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>What the business configures about rendered documents (#155), bound from <c>Billing:Documents</c>.</summary>
public sealed class DocumentOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Billing:Documents";

    /// <summary>How often the worker looks for documents to render. Operational, not a product figure.</summary>
    [Range(typeof(TimeSpan), "00:00:02", "01:00:00")]
    public TimeSpan RenderInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The most documents one pass renders, so a backlog cannot turn one run into an unbounded one.</summary>
    [Range(1, 500)]
    public int RenderBatchSize { get; set; } = 25;

    /// <summary>
    /// The terms printed at the foot of an invoice, or nothing. Product wording the business sets; no default
    /// is invented here.
    /// </summary>
    [MaxLength(1000)]
    public string? Terms { get; set; }
}
