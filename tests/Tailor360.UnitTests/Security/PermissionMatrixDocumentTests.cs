using Shouldly;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Security;

/// <summary>
/// Holds <c>docs/security/permission-matrix.md</c> equal to the code it approves.
/// </summary>
/// <remarks>
/// The matrix is the artefact the business owner signs off, and it is also the fixture these tests read.
/// That coupling is the point of the design: an approval that does not match enforcement is the failure
/// this arrangement exists to prevent, so a permission added in code and not in the document, a flag
/// changed on one side only, or a grant seeded that nobody approved all fail here rather than being
/// discovered by whoever is refused at a counter.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PermissionMatrixDocumentTests
{
    private const string Roles = "roles";
    private const string Permissions = "permissions";

    private static readonly PermissionMatrixDocument Document = PermissionMatrixDocument.Load();
    private static readonly IReadOnlyCollection<Permission> Catalogue = ApplicationPermissions.All;

    [Fact]
    public void TheDocumentIsReadAndCarriesBothBlocks()
    {
        // Guards every "every row…" assertion below against the vacuous pass: a mangled table that
        // yielded no rows would satisfy all of them.
        Document.Section(Roles).Rows.Count.ShouldBe(SystemRoles.All.Count);
        Document.Section(Permissions).Rows.Count.ShouldBe(Catalogue.Count);
        Catalogue.Count.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void EveryCataloguedPermissionHasExactlyOneRow()
    {
        var documented = Document.Section(Permissions).Rows
            .Select(row => row.Text("Permission"))
            .ToArray();

        documented.ShouldBeUnique();
        documented.Order(StringComparer.Ordinal)
            .ShouldBe(Catalogue.Select(permission => permission.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryDocumentedPermissionIsInTheCatalogue()
    {
        var catalogue = new PermissionCatalogue([new ApplicationPermissions()]);

        foreach (var row in Document.Section(Permissions).Rows)
        {
            catalogue.Contains(row.Text("Permission"))
                .ShouldBeTrue($"'{row.Text("Permission")}' is approved in the matrix and declared nowhere.");
        }
    }

    /// <summary>
    /// The assertion the whole arrangement exists for: what the owner approved and what the handler
    /// evaluates are the same three flags, permission by permission.
    /// </summary>
    [Fact]
    public void TheFlagsInTheDocumentEqualTheFlagsInTheCatalogue()
    {
        var byKey = Catalogue.ToDictionary(permission => permission.Key, StringComparer.Ordinal);

        foreach (var row in Document.Section(Permissions).Rows)
        {
            var key = row.Text("Permission");
            var permission = byKey[key];

            row.Text("Module").ShouldBe(permission.Module, key);

            // Hyphens are stripped before comparing, so the document may write 'not-branch-owned'
            // where the enum is NotBranchOwned. The alternative is a table cell reading
            // 'notbranchowned', and a document nobody wants to read is a document nobody checks.
            row.Text("Scope").Replace("-", string.Empty, StringComparison.Ordinal)
                .ShouldBe(permission.Scope.ToString().ToLowerInvariant(), key);
            row.Flag("MFA").ShouldBe(permission.RequiresMfa, key);
            row.Flag("Step-up").ShouldBe(permission.RequiresStepUp, key);
            row.Flag("Reason").ShouldBe(permission.RequiresReason, key);
        }
    }

    [Fact]
    public void EveryRoleNamedInAGrantIsADeclaredRole()
    {
        var declared = Document.Section(Roles).Rows
            .Select(row => row.Text("Role key"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in Document.Section(Permissions).Rows)
        {
            foreach (var role in row.List("Granted to"))
            {
                declared.ShouldContain(role, $"'{row.Text("Permission")}' is granted to an unknown role.");
            }
        }
    }

    [Fact]
    public void TheRoleRegisterEqualsTheSeededRoles()
    {
        var documented = Document.Section(Roles).Rows.ToDictionary(
            row => row.Text("Role key"), StringComparer.Ordinal);

        documented.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(SystemRoles.All.Select(role => role.Key).Order(StringComparer.Ordinal));

        foreach (var definition in SystemRoles.All)
        {
            var row = documented[definition.Key];

            row.Text("Display name").ShouldBe(definition.Name, definition.Key);
            row.Text("Reach").ShouldBe(definition.Reach.ToString().ToLowerInvariant(), definition.Key);
            row.Flag("Assign at onboarding").ShouldBe(definition.AssignedByDefault, definition.Key);
            row.Number("Permissions").ShouldBe(definition.Permissions.Count, definition.Key);
        }
    }

    /// <summary>
    /// The seed and the approval are the same set, read from opposite ends: the document says which
    /// roles hold a permission, the seed says which permissions a role holds.
    /// </summary>
    [Fact]
    public void TheDefaultGrantsEqualTheSeededGrants()
    {
        var documented = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var row in Document.Section(Permissions).Rows)
        {
            foreach (var role in row.List("Granted to"))
            {
                if (!documented.TryGetValue(role, out var keys))
                {
                    keys = new SortedSet<string>(StringComparer.Ordinal);
                    documented[role] = keys;
                }

                keys.Add(row.Text("Permission"));
            }
        }

        foreach (var definition in SystemRoles.All)
        {
            documented.TryGetValue(definition.Key, out var approved).ShouldBeTrue(definition.Key);
            approved!.ShouldBe(
                new SortedSet<string>(definition.Permissions, StringComparer.Ordinal),
                definition.Key);
        }
    }

    [Fact]
    public void EveryRoleHoldsSomethingAndEveryPermissionIsHeld()
    {
        foreach (var definition in SystemRoles.All)
        {
            definition.Permissions.ShouldNotBeEmpty(definition.Key);
        }

        var held = SystemRoles.All.SelectMany(role => role.Permissions).ToHashSet(StringComparer.Ordinal);
        Catalogue
            .Where(permission => !held.Contains(permission.Key))
            .Select(permission => permission.Key)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A branch role holding an organisation-scoped permission would be a branch-scoped principal with
    /// an organisation-wide power — publishing a price list, changing a template, replaying an outbound
    /// delivery. Whether that is wanted is a decision; that it is visible is not optional.
    /// </summary>
    [Fact]
    public void OrganisationScopedPermissionsAreGrantedOnlyToOrganisationRoles()
    {
        var scope = Catalogue.ToDictionary(
            permission => permission.Key, permission => permission.Scope, StringComparer.Ordinal);

        var offending = SystemRoles.All
            .Where(role => role.Reach != RoleReach.Organisation)
            .SelectMany(role => role.Permissions
                .Where(key => scope[key] == PermissionScope.Organisation)
                .Select(key => $"{role.Key} → {key}"))
            .ToArray();

        offending.ShouldBeEmpty();
    }

    [Fact]
    public void EveryPermissionRowCarriesARationale()
    {
        foreach (var row in Document.Section(Permissions).Rows)
        {
            row.Text("Rationale").Length
                .ShouldBeGreaterThan(20, $"'{row.Text("Permission")}' has no rationale worth reading.");
            row.Text("Built by").ShouldStartWith("#");
        }
    }

    [Fact]
    public void TheParserRefusesARowWithTheWrongNumberOfCells()
    {
        const string Malformed = """
            <!-- matrix:permissions -->
            | Permission | Module |
            | --- | --- |
            | `admin.users` |
            <!-- /matrix:permissions -->
            """;

        Should.Throw<InvalidOperationException>(() => PermissionMatrixDocument.Parse(Malformed))
            .Message.ShouldContain("cells");
    }

    [Fact]
    public void TheParserRefusesAnUnclosedBlock()
        => Should.Throw<InvalidOperationException>(
                () => PermissionMatrixDocument.Parse("<!-- matrix:roles -->\n| a |\n| --- |\n| b |\n"))
            .Message.ShouldContain("never closed");

    [Fact]
    public void TheParserRefusesABlockWithNoTable()
        => Should.Throw<InvalidOperationException>(
                () => PermissionMatrixDocument.Parse(
                    "<!-- matrix:roles -->\nJust some prose.\n<!-- /matrix:roles -->"))
            .Message.ShouldContain("pipe table");

    /// <summary>
    /// Asking for a block the document does not have is an error, not an empty answer.
    /// </summary>
    /// <remarks>
    /// The name below has to stay one the matrix will never have. It was <c>endpoints</c> until #24's
    /// enforcement half added that block, at which point this test started asserting that a block which
    /// exists does not — the shape of failure a placeholder name invites.
    /// </remarks>
    [Fact]
    public void TheParserRefusesAnUnknownBlockName()
        => Should.Throw<InvalidOperationException>(() => Document.Section("no-such-block"))
            .Message.ShouldContain("no 'no-such-block' block");

    [Fact]
    public void TheParserIgnoresProseOutsideTheMarkers()
    {
        var parsed = PermissionMatrixDocument.Parse("""
            # A heading nobody parses

            | Not | A | Fixture |
            | --- | --- | --- |
            | 1 | 2 | 3 |

            <!-- matrix:roles -->
            | Role key | Reach |
            | --- | --- |
            | `owner` | organisation |
            <!-- /matrix:roles -->

            More prose, and a stray pipe | character.
            """);

        parsed.Section(Roles).Rows.Count.ShouldBe(1);
        parsed.Section(Roles).Rows[0].Text("Role key").ShouldBe("owner");
    }
}
