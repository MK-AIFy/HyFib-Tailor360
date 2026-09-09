namespace Tailor360.Modules.Catalog.Contracts.Catalogue;

/// <summary>
/// Answers, for one module, whether a catalogue version may be published or retired.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the registration point the catalogue exists to offer.</strong> A service type
/// carries five links into other modules' configuration (<c>docs/prd/category-hierarchy.md</c>
/// section 5), and the Catalog module deliberately knows nothing about what is on the other end of
/// any of them: a measurement template belongs to Customers, a workflow to Orders, a price-list item
/// to Billing. Reading their tables to check a reference would be the boundary violation the whole
/// architecture is arranged to prevent, so each owning module implements this instead and the publish
/// command asks every registered validator in turn.
/// </para>
/// <para>
/// Both methods have a default that finds nothing, so a module implements only the direction it cares
/// about. #27 implements both — a publication may not reference an unpublished template, and a
/// template may not be retired while a published catalogue references it. A module that only holds
/// work in progress, such as Orders, implements only
/// <see cref="ValidateRetirementAsync"/>.
/// </para>
/// <para>
/// <strong>A validator answers about its own module and returns findings rather than throwing.</strong>
/// Every registered validator runs even when an earlier one found errors, because an administrator
/// fixing one reference at a time and re-submitting is a much worse afternoon than a single report
/// naming all of them. A validator that throws is a defect: the publish command surfaces it as an
/// unavailable dependency rather than as a validation failure, because "we could not check" is not
/// the same answer as "we checked and it is wrong".
/// </para>
/// </remarks>
public interface ICatalogDependencyValidator
{
    /// <summary>
    /// A stable name for the validator, used to attribute a finding and to name one that failed.
    /// </summary>
    string Name { get; }

    /// <summary>Whether this draft may become the published catalogue.</summary>
    /// <param name="candidate">The draft, projected into contract types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What is wrong, or an empty list.</returns>
    ValueTask<IReadOnlyList<CatalogFinding>> ValidatePublicationAsync(
        CatalogPublicationCandidate candidate,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<CatalogFinding>>([]);

    /// <summary>Whether this published version may be retired.</summary>
    /// <param name="candidate">The version being retired and what would replace it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What is wrong, or an empty list.</returns>
    ValueTask<IReadOnlyList<CatalogFinding>> ValidateRetirementAsync(
        CatalogRetirementCandidate candidate,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<CatalogFinding>>([]);
}

/// <summary>How much a finding matters.</summary>
public enum CatalogFindingSeverity
{
    /// <summary>Worth saying, and not worth stopping for. Publication proceeds.</summary>
    Warning = 0,

    /// <summary>Publication or retirement is refused while it stands.</summary>
    Error = 1,
}

/// <summary>One thing a validator found.</summary>
/// <param name="Severity">Whether it stops the command.</param>
/// <param name="Code">
/// A stable dotted code the administration screen branches on, for example
/// <c>catalog.measurement-template-not-published</c>.
/// </param>
/// <param name="Message">
/// What is wrong, in the shop's words. It becomes the detail of a field-level problem detail, so it
/// names codes, fields and rules — never a secret, and never anything about a customer.
/// </param>
/// <param name="Target">
/// Which part of the draft it is about, as a path the screen can focus: for example
/// <c>serviceTypes[BLOUSE_AARI.STITCHING].measurementTemplateId</c>. Null when the finding is about
/// the version as a whole.
/// </param>
public sealed record CatalogFinding(
    CatalogFindingSeverity Severity,
    string Code,
    string Message,
    string? Target = null)
{
    /// <summary>A finding that stops the command.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">What is wrong.</param>
    /// <param name="target">Which part of the draft it is about.</param>
    /// <returns>The finding.</returns>
    public static CatalogFinding Error(string code, string message, string? target = null)
        => new(CatalogFindingSeverity.Error, code, message, target);

    /// <summary>A finding worth saying that does not stop the command.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">What is worth saying.</param>
    /// <param name="target">Which part of the draft it is about.</param>
    /// <returns>The finding.</returns>
    public static CatalogFinding Warning(string code, string message, string? target = null)
        => new(CatalogFindingSeverity.Warning, code, message, target);
}
