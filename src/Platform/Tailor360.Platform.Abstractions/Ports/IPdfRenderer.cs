namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Renders a document to PDF. Billing renders invoices, estimates and receipts through this port
/// (#32a introduces it, #42 uses it for statutory invoices), so the rendering library stays replaceable.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>
    /// Renders a named template with the supplied model and writes the PDF to <paramref name="destination"/>.
    /// Implementations must be deterministic for a given model so that golden-master tests are stable.
    /// </summary>
    Task<Results.Result> RenderAsync(
        string templateKey,
        IReadOnlyDictionary<string, object?> model,
        Stream destination,
        CancellationToken cancellationToken = default);
}
