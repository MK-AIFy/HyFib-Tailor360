using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The stand-ins the measurement-template handler tests run against.
/// </summary>
/// <remarks>
/// They exist for one narrow purpose: to make the store's commit <em>fail</em> on demand. Losing the row between
/// the precondition being checked and the commit landing is a real race, but it is a race no integration test can
/// stage reliably — the window is a few milliseconds wide inside one request. A fake store closes it deliberately,
/// which is the only way to assert that the handler answers a stale write rather than letting it escape as a fault.
/// </remarks>
internal sealed class StubTemplateStore(MeasurementTemplate template) : IMeasurementTemplateStore
{
    /// <summary>What the next <see cref="SaveAsync"/> answers. Success unless a test says otherwise.</summary>
    public Result NextSave { get; set; } = Result.Success();

    /// <summary>How many times a commit was attempted, so a test can prove one was.</summary>
    public int Saves { get; private set; }

    public Task<MeasurementTemplate?> FindAsync(
        Guid templateId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<MeasurementTemplate?>(
            template.Id == templateId && template.OrganisationId == organisationId ? template : null);

    public Task<MeasurementTemplate?> FindByCodeAsync(
        string code,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<MeasurementTemplate?>(
            string.Equals(template.Code, code, StringComparison.Ordinal) ? template : null);

    public Task<MeasurementTemplate?> FindByVersionAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<MeasurementTemplate?>(
            template.Versions.Any(version => version.Id == versionId) ? template : null);

    public Task<IReadOnlyList<MeasurementTemplate>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MeasurementTemplate>>([template]);

    public Task<bool> AnyAsync(Guid organisationId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public void Add(MeasurementTemplate added)
    {
        // Nothing to do: this double holds exactly the one template it was built around.
    }

    public EntityTag EntityTagOf(MeasurementTemplate subject) => new("1");

    public Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        Saves++;

        return Task.FromResult(NextSave);
    }

    public Task<Result> SaveNewAsync(CancellationToken cancellationToken = default)
    {
        Saves++;

        return Task.FromResult(NextSave);
    }

    public Task<Result> SavePublicationAsync(CancellationToken cancellationToken = default)
    {
        Saves++;

        return Task.FromResult(NextSave);
    }
}

/// <summary>A directory that answers a fixed number of administrators who could approve.</summary>
internal sealed class StubUserDirectory(int activeWithPermission) : IUserDirectory
{
    public Task<StaffMember?> FindAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<StaffMember?>(null);

    public Task<IReadOnlyList<StaffMember>> FindManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StaffMember>>([]);

    public Task<IReadOnlyList<StaffMember>> ListActiveInBranchAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StaffMember>>([]);

    public Task<int> CountActiveWithPermissionAsync(
        string permissionKey,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(activeWithPermission);
}

/// <summary>A catalogue that either does or does not still point at the template.</summary>
internal sealed class StubCatalogAvailability(bool referencesTemplate) : ICatalogAvailabilityQuery
{
    public Task<bool> IsOrderableAsync(
        Guid serviceTypeId,
        Guid branchId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<CatalogServiceSnapshot?> GetServiceAsync(
        Guid serviceTypeId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<CatalogServiceSnapshot?>(null);

    public Task<OrderableCatalog> GetOrderableCatalogAsync(
        Guid organisationId,
        Guid branchId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OrderableCatalog.None);

    public Task<bool> ReferencesMeasurementTemplateAsync(
        Guid measurementTemplateId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(referencesTemplate);

    public Task<IReadOnlyList<CatalogPriceListItemReference>> PublishedPriceListItemReferencesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CatalogPriceListItemReference>>([]);
}

/// <summary>
/// Collects what the handler published, so a test can assert an event was staged with the change.
/// </summary>
/// <remarks>
/// It records rather than sends, because what a test about this handler cares about is that the event was added
/// to the unit of work at all — the outbox's own behaviour is the integration tier's subject.
/// </remarks>
internal sealed class RecordingEventPublisher : ICustomersEventPublisher
{
    private readonly List<IIntegrationEvent> _published = [];

    /// <summary>Everything staged, in the order it was staged.</summary>
    public IReadOnlyList<IIntegrationEvent> Published => _published;

    /// <inheritdoc />
    public void Publish(IIntegrationEvent integrationEvent) => _published.Add(integrationEvent);
}
