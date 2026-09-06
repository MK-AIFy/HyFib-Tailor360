using System.Reflection;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.Background;

/// <summary>
/// A background job's declaration, read from its <see cref="WorkerJobAttribute"/> and checked against
/// the permission catalogue.
/// </summary>
/// <remarks>
/// Resolving the declaration is where a mistake is caught: a job with no attribute cannot obtain a
/// scope, and a job naming a permission no module declares fails on the first run rather than silently
/// holding a key that authorises nothing. The architecture test asserts the same two things over the
/// worker assembly, so neither has to wait for a run to be noticed.
/// </remarks>
/// <param name="Name">The stable job name.</param>
/// <param name="BranchScope">How far across branches the job reaches.</param>
/// <param name="Permissions">The permission keys the job exercises.</param>
/// <param name="ActsForRequester">True when the job runs for the user who queued it.</param>
public sealed record WorkerJobDescriptor(
    string Name,
    WorkerBranchScope BranchScope,
    IReadOnlySet<string> Permissions,
    bool ActsForRequester)
{
    /// <summary>Reads the declaration from a job type, or null when the type carries none.</summary>
    /// <param name="jobType">The job type.</param>
    public static WorkerJobDescriptor? Find(Type jobType)
    {
        ArgumentNullException.ThrowIfNull(jobType);

        var attribute = jobType.GetCustomAttribute<WorkerJobAttribute>(inherit: false);
        if (attribute is null)
        {
            return null;
        }

        return new WorkerJobDescriptor(
            attribute.Name,
            attribute.BranchScope,
            attribute.Permissions.ToHashSet(StringComparer.Ordinal),
            attribute.ActsForRequester);
    }

    /// <summary>Reads the declaration from a job type.</summary>
    /// <param name="jobType">The job type.</param>
    /// <exception cref="InvalidOperationException">The type carries no declaration.</exception>
    public static WorkerJobDescriptor For(Type jobType)
        => Find(jobType)
           ?? throw new InvalidOperationException(
               $"'{jobType?.FullName}' asked for a worker scope without declaring one. Add "
               + "[WorkerJob(name, scope, permissions)] to the type: a background job runs with no "
               + "ambient user, so what it may do has to be stated rather than inherited.");

    /// <summary>
    /// Checks the declaration against the catalogue and against itself, and throws when the deployment
    /// could not honour it.
    /// </summary>
    /// <param name="catalogue">The permission catalogue.</param>
    /// <exception cref="InvalidOperationException">The declaration cannot be honoured.</exception>
    public void Validate(PermissionCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var unknown = Permissions
            .Where(key => !catalogue.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"Worker job '{Name}' declares {string.Join(", ", unknown)}, which no module declares. "
                + "A job cannot hold a permission that does not exist, and a typo here would leave the "
                + "job holding nothing while appearing to hold something.");
        }

        if (!ActsForRequester && BranchScope == WorkerBranchScope.AssignedBranches)
        {
            throw new InvalidOperationException(
                $"Worker job '{Name}' runs as the system and declares the branch scope of a requester. "
                + "The system is assigned to no branch; declare None, OneBranch or Organisation.");
        }

        if (!ActsForRequester)
        {
            return;
        }

        var stepUp = Permissions
            .Select(catalogue.Find)
            .Where(permission => permission is { RequiresStepUp: true })
            .Select(permission => permission!.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        if (stepUp.Length > 0)
        {
            throw new InvalidOperationException(
                $"Worker job '{Name}' acts for its requester and declares {string.Join(", ", stepUp)}, "
                + "which demands a re-authentication in the last few minutes. A queued job cannot be "
                + "fresh by the time it runs, so the action belongs in the request that asked for it.");
        }
    }
}
