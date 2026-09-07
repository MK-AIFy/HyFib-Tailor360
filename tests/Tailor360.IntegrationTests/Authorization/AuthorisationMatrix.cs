using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Permissions;
using Tailor360.UnitTests.Security;

namespace Tailor360.IntegrationTests.Authorization;

/// <summary>
/// Holds the owner-approved permission matrix and the route table the application actually publishes
/// equal to each other.
/// </summary>
/// <remarks>
/// <para>
/// This is the coupling the whole design exists for. The matrix document is what a person approves; the
/// route table is what the server enforces. Left as two artefacts they drift within a release — an
/// endpoint is added and nobody adds its row, a row is approved and nobody builds the endpoint, a
/// permission is quietly swapped for a laxer one. Reconciling them mechanically means the approval and
/// the enforcement cannot say different things without a build failing.
/// </para>
/// <para>
/// It is a function over two collections rather than a test body so that the same rules can be applied
/// to deliberately wrong inputs. A detector nobody has seen detect anything is a detector nobody should
/// trust, and the failure this one guards against — a route that escapes the matrix — is by its nature
/// invisible until it matters.
/// </para>
/// </remarks>
public static class AuthorisationMatrix
{
    /// <summary>The name of the marked block in the matrix document that lists routes.</summary>
    public const string EndpointsSection = "endpoints";

    /// <summary>The name of the marked block that lists permissions.</summary>
    public const string PermissionsSection = "permissions";

    /// <summary>The name of the marked block that lists roles.</summary>
    public const string RolesSection = "roles";

    /// <summary>Reads the endpoint rows of a matrix document.</summary>
    /// <exception cref="InvalidOperationException">A cell holds something the matrix cannot mean.</exception>
    public static IReadOnlyList<MatrixEndpointRow> EndpointRows(PermissionMatrixDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return
        [
            .. document.Section(EndpointsSection).Rows.Select(row => new MatrixEndpointRow(
                row.Text("Method"),
                row.Text("Route"),
                row.Text("Declaration"),
                Optional(row.Text("Permission")),
                ScopeFrom(row.Text("Branch scope")),
                Optional(row.Text("Resource")),
                Optional(row.Text("Audited as")))),
        ];
    }

    /// <summary>
    /// Every way the matrix and the route table disagree, as sentences a reader can act on.
    /// </summary>
    /// <param name="rows">The rows of the matrix document's endpoint block.</param>
    /// <param name="endpoints">The routes the application publishes.</param>
    /// <param name="catalogue">The permission catalogue.</param>
    /// <param name="approvedPermissions">The permission keys the matrix's own permission block lists.</param>
    public static IReadOnlyList<string> Reconcile(
        IReadOnlyList<MatrixEndpointRow> rows,
        IReadOnlyList<EndpointDeclaration> endpoints,
        PermissionCatalogue catalogue,
        IReadOnlySet<string> approvedPermissions)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(approvedPermissions);

        var complaints = new List<string>();
        var bySignature = new Dictionary<string, MatrixEndpointRow>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!bySignature.TryAdd(row.Signature, row))
            {
                complaints.Add(
                    $"The matrix lists '{row.Signature}' more than once. One route, one approved row: two "
                    + "rows for one route means half the reviewers read the wrong one.");
            }
        }

        // Grouped rather than keyed directly, because two routes can answer the same method and template
        // — a group applied twice, a fallback re-registered — and that is a complaint about the route
        // table, not an exception from the test that was trying to describe it.
        var published = new Dictionary<string, EndpointDeclaration>(StringComparer.Ordinal);

        foreach (var endpoint in endpoints)
        {
            if (!published.TryAdd(endpoint.Signature, endpoint))
            {
                complaints.Add(
                    $"The application publishes '{endpoint.Signature}' more than once. Only one of them "
                    + "can ever be reached, and the matrix cannot say which was approved.");
            }
        }

        complaints.AddRange(
            from endpoint in endpoints
            where !bySignature.ContainsKey(endpoint.Signature)
            select $"{endpoint.Signature} is published by the application and has no row in the "
                   + "authorisation matrix. Add it to the endpoints block of "
                   + $"{PermissionMatrixDocument.RelativePath}, declaring "
                   + $"'{endpoint.DeclarationText}', so the exposure is approved rather than merely present.");

        complaints.AddRange(
            from row in rows
            where !published.ContainsKey(row.Signature)
            select $"The matrix approves '{row.Signature}', which the application does not publish. A row "
                   + "with no route is an approval nobody is relying on, and it hides the route that was "
                   + "renamed out from under it.");

        foreach (var row in rows.Where(row => published.ContainsKey(row.Signature)))
        {
            complaints.AddRange(Compare(row, published[row.Signature], catalogue, approvedPermissions));
        }

        return complaints;
    }

    private static IEnumerable<string> Compare(
        MatrixEndpointRow row,
        EndpointDeclaration endpoint,
        PermissionCatalogue catalogue,
        IReadOnlySet<string> approvedPermissions)
    {
        var name = row.Signature;

        if (endpoint.Kind == EndpointDeclarationKind.Undeclared)
        {
            yield return
                $"{name} declares neither a permission, a justified anonymous exposure nor a self-service "
                + "assurance level. It is reachable and nobody has said by whom.";
        }

        if (!string.Equals(row.Declaration, endpoint.DeclarationText, StringComparison.Ordinal))
        {
            yield return
                $"{name} is approved as '{row.Declaration}' and enforced as '{endpoint.DeclarationText}'.";
        }

        if (!string.Equals(row.Permission, endpoint.PermissionKey, StringComparison.Ordinal))
        {
            yield return
                $"{name} is approved to demand '{row.Permission ?? "no permission"}' and demands "
                + $"'{endpoint.PermissionKey ?? "no permission"}'.";
        }

        if (row.Scope != endpoint.Scope)
        {
            yield return
                $"{name} is approved with branch scope '{Describe(row.Scope)}' and declares "
                + $"'{Describe(endpoint.Scope)}'. How far a permission reaches is the other half of what "
                + "granting it means.";
        }

        if (!string.Equals(row.Resource, endpoint.ResourceKind, StringComparison.Ordinal))
        {
            yield return
                $"{name} is approved against resource '{row.Resource ?? "none"}' and declares "
                + $"'{endpoint.ResourceKind ?? "none"}'.";
        }

        if (!string.Equals(row.AuditAction, endpoint.AuditAction, StringComparison.Ordinal))
        {
            yield return
                $"{name} is approved to be audited as '{row.AuditAction ?? "not audited"}' and is audited "
                + $"as '{endpoint.AuditAction ?? "not audited"}'.";
        }

        if (row.Permission is not { } key)
        {
            yield break;
        }

        if (!catalogue.Contains(key))
        {
            yield return
                $"{name} is approved to demand '{key}', which no module declares. A permission nobody "
                + "declares is refused to everybody, so this closes the route rather than opening it — but "
                + "it closes it silently.";

            yield break;
        }

        if (!approvedPermissions.Contains(key))
        {
            yield return
                $"{name} demands '{key}', which has no row in the matrix's permission block. The endpoint "
                + "is approved and the thing it demands is not.";
        }

        // ARCH-018, checked where both facts are visible: the catalogue's flag and the endpoint's own
        // declaration. The permission handler already refuses a stale session, so this is not what makes
        // step-up work; it is what stops the demand from being invisible to everyone reading the route,
        // the API document or this matrix.
        if (catalogue.Find(key)?.RequiresStepUp == true && !endpoint.StepUpDeclared)
        {
            yield return
                $"ARCH-018: {name} demands '{key}', which is flagged for step-up, and the endpoint does "
                + "not declare .RequireStepUp(). The refusal would still happen; nobody reading the "
                + "endpoint would know it was going to.";
        }

        if (catalogue.Find(key)?.RequiresStepUp == false && endpoint.StepUpDeclared)
        {
            yield return
                $"{name} declares .RequireStepUp() and demands '{key}', which is not flagged for step-up. "
                + "Either the flag belongs on the permission — where every endpoint using it gets it — or "
                + "the declaration does not belong here.";
        }
    }

    private static string? Optional(string cell) => cell is "—" or "" ? null : cell;

    private static string Describe(BranchScope? scope) => scope is { } value ? Hyphenate(value.ToString()) : "none";

    private static BranchScope? ScopeFrom(string cell) => cell switch
    {
        "—" or "" => null,
        "current-branch" => BranchScope.CurrentBranch,
        "assigned-branches" => BranchScope.AssignedBranches,
        "organisation" => BranchScope.Organisation,
        "not-branch-owned" => BranchScope.NotBranchOwned,
        _ => throw new InvalidOperationException(
            $"'{cell}' is not a branch scope. The endpoints block accepts 'current-branch', "
            + "'assigned-branches', 'organisation', 'not-branch-owned' or an em dash."),
    };

    private static string Hyphenate(string pascalCase)
        => string.Concat(pascalCase.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? "-" + char.ToLowerInvariant(character)
                : char.ToLowerInvariant(character).ToString()));
}

/// <summary>One approved row of the matrix document's endpoint block.</summary>
/// <param name="Method">The HTTP method, or <c>ANY</c> for a route that answers every verb.</param>
/// <param name="Route">The route template with constraints stripped.</param>
/// <param name="Declaration">How the route decides who may reach it.</param>
/// <param name="Permission">The permission demanded, or null.</param>
/// <param name="Scope">The branch reach approved alongside it, or null.</param>
/// <param name="Resource">The resource the answer is decided against, or null.</param>
/// <param name="AuditAction">The audit action, or null when the route is not audited.</param>
public sealed record MatrixEndpointRow(
    string Method,
    string Route,
    string Declaration,
    string? Permission,
    BranchScope? Scope,
    string? Resource,
    string? AuditAction)
{
    /// <summary>How this row names its route.</summary>
    public string Signature => $"{Method} {Route}";
}
