using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Customers.Infrastructure.Measurements;

/// <summary>
/// Which branch is measuring a garment (issue #121).
/// </summary>
/// <remarks>
/// <para>
/// <strong>This module's first resource resolver, and it does not contradict the module's position.</strong> The
/// registration extension explains why a <em>customer</em> has none: a customer served at one branch who walks
/// into another is the same customer, and refusing the second branch is how a duplicate record gets created. A
/// <em>draft</em> is the opposite shape. It is half a garment, shared within the branch measuring it
/// (<c>docs/architecture/invariants.md</c> section 4.3), and another branch writing to it would be overwriting
/// measurements taken with a tape somebody else is holding.
/// </para>
/// <para>
/// It applies no branch filter of its own. Returning null for a draft that exists at another branch would make
/// "not yours" and "not there" this class's decision to confuse; the pipeline decides that, and the pipeline is
/// where the audit trail is written.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class MeasurementDraftScopeResolver(CustomersDbContext context) : IResourceScopeResolver
{
    /// <inheritdoc />
    public string ResourceKind => MeasurementResourceKinds.MeasurementDraft;

    /// <inheritdoc />
    public async ValueTask<ResourceScope?> ResolveAsync(
        Guid resourceId,
        CancellationToken cancellationToken)
    {
        // One indexed read of the owning row, and only the column that decides. A draft carries no notion of
        // assignment: whoever is at the counter measures.
        var branchId = await context.MeasurementDrafts
            .Where(draft => draft.Id == resourceId)
            .Select(draft => (Guid?)draft.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        return branchId is { } branch ? ResourceScope.Unassigned(MeasurementResourceKinds.MeasurementDraft, resourceId, branch) : null;
    }
}
