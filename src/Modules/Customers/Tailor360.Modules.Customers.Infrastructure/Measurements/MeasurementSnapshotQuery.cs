using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Contracts.Measurements;
using Tailor360.Modules.Customers.Infrastructure.Persistence;

namespace Tailor360.Modules.Customers.Infrastructure.Measurements;

/// <summary>
/// The published read of a confirmed measurement (issue #122).
/// </summary>
/// <remarks>
/// <para>
/// Reads only. There is no write on this contract and there will not be one: a measurement is created by the
/// person who took it, through the capture routes, with the consent check and the audit entry that go with it.
/// A module that could create one from the inside would be a second way to record evidence about a customer.
/// </para>
/// <para>
/// It applies no branch filter. Which callers may read a measurement is the endpoint's decision and the
/// authorisation pipeline's, and the pipeline is where the audit trail is written; a query that quietly returned
/// null for another branch's measurement would make "not yours" and "not there" indistinguishable to the caller
/// and invisible to the trail.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class MeasurementSnapshotQuery(CustomersDbContext context) : IMeasurementSnapshotQuery
{
    /// <inheritdoc />
    public async Task<MeasurementSnapshot?> GetAsync(
        Guid measurementVersionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var version = await context.MeasurementVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                one => one.Id == measurementVersionId && one.OrganisationId == organisationId,
                cancellationToken);

        return version is null
            ? null
            : new MeasurementSnapshot(
                version.Id,
                version.CustomerId,
                version.BranchId,
                version.TemplateId,
                version.TemplateVersionId,
                version.VersionNumber,
                version.TakenAt,
                version.TakenBy,
                version.CorrectsVersionId,
                [
                    .. version.Values.Select(value => new MeasuredValueSnapshot(
                        value.Key.Value,
                        value.Millimetres,
                        value.EnteredUnit.ToString(),
                        value.Choice,
                        value.Acknowledged)),
                ]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> ExistingAsync(
        IReadOnlyCollection<Guid> measurementVersionIds,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(measurementVersionIds);

        if (measurementVersionIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var found = await context.MeasurementVersions
            .AsNoTracking()
            .Where(version => measurementVersionIds.Contains(version.Id)
                              && version.OrganisationId == organisationId)
            .Select(version => version.Id)
            .ToListAsync(cancellationToken);

        return found.ToHashSet();
    }
}
