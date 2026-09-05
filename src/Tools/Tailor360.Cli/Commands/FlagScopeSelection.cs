using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Cli.Commands;

/// <summary>
/// Validates the scope arguments of <c>flags set</c> and produces the scope identity that is stored.
/// Extracted from the command so the rules can be tested without running the process: getting them
/// wrong writes a row the evaluator reads as something the operator did not ask for.
/// </summary>
public static class FlagScopeSelection
{
    /// <summary>
    /// Checks the combination of scope and branch, returning the identity to store on success.
    /// </summary>
    /// <param name="scopeType">The scope the operator asked for.</param>
    /// <param name="branchId">The branch supplied, if any.</param>
    /// <param name="scopeId">The identity to store when the combination is valid.</param>
    /// <param name="error">The message to print when it is not.</param>
    public static bool TryResolve(string scopeType, Guid? branchId, out Guid scopeId, out string? error)
    {
        scopeId = Guid.Empty;
        error = null;

        if (scopeType is not (FeatureFlagScopes.Branch or FeatureFlagScopes.Organisation))
        {
            error = "--scope must be 'organisation' or 'branch'.";
            return false;
        }

        if (scopeType == FeatureFlagScopes.Branch)
        {
            if (branchId is null)
            {
                error = "A branch-scoped value needs --branch <guid>.";
                return false;
            }

            scopeId = branchId.Value;
            return true;
        }

        if (branchId is not null)
        {
            // Storing the branch against an organisation row would give the evaluator a value it reads
            // as organisation-wide, silently enabling the feature everywhere the operator did not ask
            // for; a later, correctly scoped organisation row would then collide with it.
            error = "--branch is not accepted with --scope organisation. Use --scope branch to set a " +
                    "value for one branch, or drop --branch to set the organisation value.";
            return false;
        }

        // Organisation rows are always keyed by the empty identity, so the store can never hold two
        // rows the evaluator would treat as the same value.
        scopeId = Guid.Empty;
        return true;
    }
}
