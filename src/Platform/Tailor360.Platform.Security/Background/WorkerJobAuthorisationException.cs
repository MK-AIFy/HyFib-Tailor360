namespace Tailor360.Platform.Security.Background;

/// <summary>
/// Thrown when a job that acts for a requester may not run, because the requester's authority today is
/// not the authority they had when they queued it.
/// </summary>
/// <remarks>
/// This is the difference between a queue and a grant. A dispatch queued yesterday by someone who has
/// since been suspended, moved out of the branch, or had the permission taken away must not run with
/// the authority they used to hold — the whole point of removing access is that it takes effect on
/// work not yet done. The refusal is deliberately an abort rather than a retry: nothing about waiting
/// longer will make the requester authorised again, and a retry loop would turn a revocation into a
/// stream of identical failures.
/// </remarks>
public sealed class WorkerJobAuthorisationException : Exception
{
    /// <summary>The stable code recorded for this refusal.</summary>
    public const string Code = "job.requester-unauthorised";

    /// <summary>Creates the exception with the default message.</summary>
    public WorkerJobAuthorisationException()
        : this(string.Empty, Guid.Empty, WorkerJobRefusal.UnknownRequester)
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public WorkerJobAuthorisationException(string message)
        : base(message)
    {
        JobName = string.Empty;
        Reason = WorkerJobRefusal.UnknownRequester;
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public WorkerJobAuthorisationException(string message, Exception innerException)
        : base(message, innerException)
    {
        JobName = string.Empty;
        Reason = WorkerJobRefusal.UnknownRequester;
    }

    /// <summary>Creates the exception for a named job and requester.</summary>
    /// <param name="jobName">The job that was refused.</param>
    /// <param name="requesterId">The account the job would have run for.</param>
    /// <param name="reason">Why the job was refused.</param>
    public WorkerJobAuthorisationException(string jobName, Guid requesterId, WorkerJobRefusal reason)
        : base($"{Code}: job '{jobName}' was not started for its requester ({Describe(reason)}).")
    {
        JobName = jobName;
        RequesterId = requesterId;
        Reason = reason;
    }

    /// <summary>The job that was refused.</summary>
    public string JobName { get; } = string.Empty;

    /// <summary>
    /// The account the job would have run for. An identifier only: the message carries no name, no
    /// contact detail and no permission list, because a refusal is logged and a log holds neither.
    /// </summary>
    public Guid RequesterId { get; }

    /// <summary>Why the job was refused.</summary>
    public WorkerJobRefusal Reason { get; }

    private static string Describe(WorkerJobRefusal reason) => reason switch
    {
        WorkerJobRefusal.UnknownRequester => "the account no longer exists",
        WorkerJobRefusal.RequesterNotActive => "the account is no longer active",
        WorkerJobRefusal.PermissionNotHeld => "a permission the job needs is no longer held",
        WorkerJobRefusal.SecondFactorNotSatisfied =>
            "the job needs a permission that demands multi-factor authentication, which the session "
            + "that queued it had not satisfied",
        WorkerJobRefusal.BranchNotAssigned => "the branch is no longer one the account is assigned to",
        WorkerJobRefusal.NoBranchAssigned => "the account is assigned to no branch",
        WorkerJobRefusal.OrganisationReachNotHeld => "the account no longer reads across branches",
        _ => "the requester is not authorised",
    };
}

/// <summary>Why a job acting for a requester was refused.</summary>
public enum WorkerJobRefusal
{
    /// <summary>No account with that identifier exists any more.</summary>
    UnknownRequester = 0,

    /// <summary>The account exists but is suspended, deactivated or otherwise not able to act.</summary>
    RequesterNotActive = 1,

    /// <summary>The account no longer holds one of the permissions the job declares.</summary>
    PermissionNotHeld = 2,

    /// <summary>The job needs a multi-factor permission and the queueing session had not satisfied one.</summary>
    SecondFactorNotSatisfied = 3,

    /// <summary>The branch the job names is not one the account is assigned to any more.</summary>
    BranchNotAssigned = 4,

    /// <summary>The job reaches the account's branches and the account is now assigned to none.</summary>
    NoBranchAssigned = 5,

    /// <summary>The job reaches the organisation and the account no longer holds that reach.</summary>
    OrganisationReachNotHeld = 6,
}
