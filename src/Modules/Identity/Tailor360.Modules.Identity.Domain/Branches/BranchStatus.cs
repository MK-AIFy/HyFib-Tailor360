namespace Tailor360.Modules.Identity.Domain.Branches;

/// <summary>Whether a branch may be worked in.</summary>
/// <remarks>
/// A branch is never deleted. Order numbers, invoice sequences, custody events and audit entries all
/// name it, and a deleted branch would make that history unreadable; a closed shop is deactivated.
/// </remarks>
public enum BranchStatus
{
    /// <summary>Open for business.</summary>
    Active = 0,

    /// <summary>Closed. Historical references stay resolvable; nothing new may be booked to it.</summary>
    Inactive = 1,
}
