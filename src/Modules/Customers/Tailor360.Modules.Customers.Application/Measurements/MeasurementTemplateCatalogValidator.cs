using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Customers.Contracts.Measurements;

namespace Tailor360.Modules.Customers.Application.Measurements;

/// <summary>
/// Customers' answer to "may this catalogue version be published?".
/// </summary>
/// <remarks>
/// <para>
/// A catalogue service type carries a measurement template — link 1 of the five
/// (<c>docs/prd/category-hierarchy.md</c> section 5). Catalog deliberately knows nothing about what is on the
/// other end, so this is where the reference is actually checked, in the module that owns the templates. Reading
/// the template tables from Catalog would be the boundary violation the registration point exists to avoid.
/// </para>
/// <para>
/// <strong>The check is that the template has a published version, not that the template exists.</strong> A
/// service type pointing at a template whose only version is still a draft would be orderable at the counter with
/// nothing to measure it by — the tailor would be handed a job and no field set. That is worth refusing a
/// publication over, and it is the error a shop most often hits in the order these features are being built:
/// the catalogue is drafted first and the templates follow.
/// </para>
/// <para>
/// The other direction — refusing to retire a template while a published catalogue points at it — is not here.
/// It belongs to the retirement command in <see cref="MeasurementTemplateHandler"/>, because that is where the
/// act happens; this interface is asked about the catalogue's lifecycle, not this module's.
/// </para>
/// </remarks>
/// <param name="templates">The module's published template query.</param>
public sealed class MeasurementTemplateCatalogValidator(IMeasurementTemplateQuery templates)
    : ICatalogDependencyValidator
{
    /// <summary>The name findings from this validator are attributed to.</summary>
    public const string ValidatorName = "measurement-templates";

    /// <inheritdoc />
    public string Name => ValidatorName;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CatalogFinding>> ValidatePublicationAsync(
        CatalogPublicationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var referencing = candidate.ServiceTypes
            .Where(service => service.MeasurementTemplateId is not null)
            .ToArray();

        if (referencing.Length == 0)
        {
            return [];
        }

        // One batched question rather than one per service type: a catalogue with sixty services would otherwise
        // make sixty round trips every time somebody pressed publish.
        var ready = await templates.WithPublishedVersionAsync(
            [.. referencing.Select(service => service.MeasurementTemplateId!.Value).Distinct()],
            candidate.OrganisationId,
            cancellationToken);

        var categoryCodes = candidate.Categories.ToDictionary(
            category => category.Id, category => category.Code);

        return
        [
            .. referencing
                .Where(service => !ready.Contains(service.MeasurementTemplateId!.Value))
                .Select(service => new CatalogFinding(
                    CatalogFindingSeverity.Error,
                    "catalog.measurement-template-not-published",
                    $"The measurement template linked to '{Reference(categoryCodes, service)}' has no published "
                    + "version, so the counter could order this service with nothing to measure it by. Publish "
                    + "the template version first.",
                    $"serviceTypes[{Reference(categoryCodes, service)}].measurementTemplateId")),
        ];
    }

    private static string Reference(
        Dictionary<Guid, string> categoryCodes,
        CatalogServiceTypeView service)
        => categoryCodes.TryGetValue(service.CategoryId, out var code)
            ? $"{code}.{service.Code}"
            : service.Code;
}
