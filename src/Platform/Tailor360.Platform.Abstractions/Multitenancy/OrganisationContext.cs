namespace Tailor360.Platform.Abstractions.Multitenancy;

/// <summary>
/// The organisation and branch a unit of work belongs to. Resolved per request from the session and
/// per background job from the job's stored context, so worker code is never branch-blind.
/// </summary>
/// <param name="OrganisationId">The single organisation this deployment serves.</param>
/// <param name="BranchId">The branch the work belongs to, when the work is branch-scoped.</param>
public sealed record OrganisationContext(Guid OrganisationId, Guid? BranchId)
{
    /// <summary>True when the work is bound to one branch.</summary>
    public bool IsBranchScoped => BranchId is not null;
}
