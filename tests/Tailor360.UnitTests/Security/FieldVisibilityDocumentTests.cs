using Shouldly;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Platform.Security.FieldVisibility;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Security;

/// <summary>
/// Holds <c>docs/security/field-visibility.md</c> equal to the views the application declares and to the
/// grants the roles ship with.
/// </summary>
/// <remarks>
/// <para>
/// The per-role tables in that document are not written by hand and then checked. They are <b>derived</b>
/// from the view catalogue and the seeded role grants, and the document is asserted to carry exactly the
/// derivation. That is what makes the document usable as a fixture by the later pull requests that add
/// endpoints: they read the approved field set for a role, and the set is by construction the one the
/// code will produce.
/// </para>
/// <para>
/// When one of these fails it prints the whole derived table, so the fix is to paste it in — never to
/// loosen the assertion.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FieldVisibilityDocumentTests
{
    private static readonly ResponseViewCatalogue Catalogue = new([new ApplicationResponseViews()]);
    private static readonly PermissionMatrixDocument Document = FieldVisibilityDocument.Load();

    [Fact]
    public void TheDocumentDeclaresEveryViewTheApplicationDeclaresAndNoOthers()
    {
        var rows = Document.Section("views").Rows;

        rows.Count.ShouldBe(Catalogue.All.Count, "the views block and the catalogue differ in size");
        rows.Select(row => row.Text("View")).ShouldBe(
            Catalogue.All.Select(view => view.Key), ignoreOrder: true);
    }

    [Fact]
    public void EveryViewsModulePermissionAndWithheldClassesMatchTheCode()
    {
        foreach (var row in Document.Section("views").Rows)
        {
            var view = Catalogue.Find(row.Text("View"));
            view.ShouldNotBeNull($"the document declares a view '{row.Text("View")}' the code does not");

            row.Text("Module").ShouldBe(view.Module, view.Key);
            row.Text("Surface").ShouldBe(view.Surface.ToString(), view.Key);
            row.Text("Requires").ShouldBe(view.RequiredPermission, view.Key);
            row.List("Withheld").ShouldBe(WithheldNames(view.Withheld), ignoreOrder: true, view.Key);
        }
    }

    [Fact]
    public void EveryDeclaredFieldHasOneRowAndEveryRowHasOneField()
    {
        var declared = Catalogue.All
            .SelectMany(view => view.Fields.Select(field => $"{view.Key}.{field.Name}"))
            .ToArray();

        var rows = Document.Section("fields").Rows;
        var documented = rows.Select(row => $"{row.Text("View")}.{row.Text("Field")}").ToArray();

        documented.Length.ShouldBe(declared.Length, "the fields block and the catalogue differ in size");
        documented.ShouldBe(declared, ignoreOrder: true);
    }

    [Fact]
    public void EveryFieldsClassAndRequiredPermissionMatchTheCode()
    {
        // This is the clause the design exists for. A field approved as "operational, visible to
        // everyone who can open the view" and implemented as sensitive personal data behind a permission
        // — or the reverse, which is the dangerous direction — is caught here and nowhere else.
        var mismatches = new List<string>();

        foreach (var row in Document.Section("fields").Rows)
        {
            var view = Catalogue.Require(row.Text("View"));
            var field = view.Field(row.Text("Field"));
            if (field is null)
            {
                mismatches.Add($"{view.Key}.{row.Text("Field")}: the code declares no such field");
                continue;
            }

            var documentedClass = row.Text("Class");
            if (documentedClass != field.Classification.ToString())
            {
                mismatches.Add(
                    $"{view.Key}.{field.Name}: approved as {documentedClass}, "
                    + $"declared as {field.Classification}");
            }

            var documentedPermission = row.Text("Requires");
            var codePermission = field.RequiredPermission ?? "—";
            if (documentedPermission != codePermission)
            {
                mismatches.Add(
                    $"{view.Key}.{field.Name}: approved behind '{documentedPermission}', "
                    + $"declared behind '{codePermission}'");
            }
        }

        mismatches.ShouldBeEmpty(
            "the approved field sets and the code disagree:\n" + string.Join('\n', mismatches));
    }

    [Fact]
    public void EveryPermissionTheDocumentNamesIsInTheCatalogue()
    {
        var permissions = new PermissionCatalogue([new ApplicationPermissions()]);

        var named = Document.Section("views").Rows.Select(row => row.Text("Requires"))
            .Concat(Document.Section("fields").Rows.Select(row => row.Text("Requires")))
            .Where(value => value != "—")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        named.Length.ShouldBeGreaterThan(0, "the document names no permission at all, which cannot be right");
        named.Where(key => !permissions.Contains(key)).ShouldBeEmpty();
    }

    [Fact]
    public void TheRoleTableIsExactlyWhatTheCodeAndTheSeededGrantsProduce()
    {
        var expected = DerivedRoleRows();
        var actual = Document.Section("role-fields").Rows
            .Select(row => Row(
                row.Text("View"),
                row.Text("Role"),
                row.Flag("Reaches"),
                row.List("Visible fields")))
            .ToArray();

        actual.Length.ShouldBeGreaterThan(0);
        actual.ShouldBe(expected, ignoreOrder: true, RenderExpected(expected));
    }

    [Fact]
    public void EveryRoleTheDocumentNamesIsASystemRole()
    {
        var known = SystemRoles.All.Select(role => role.Key).ToHashSet(StringComparer.Ordinal);
        var named = Document.Section("role-fields").Rows.Select(row => row.Text("Role")).Distinct(StringComparer.Ordinal);

        named.Where(role => !known.Contains(role)).ShouldBeEmpty();
    }

    [Fact]
    public void NoWorkshopSurfaceIsApprovedToCarryContactPricingOrPaymentState()
    {
        // Asserted against the document as well as against the code, because this is the sentence the
        // owner is approving and it must be legible in the artefact they approved.
        //
        // It is scoped to the workshop surfaces because that is the scope of the sentence. The one it
        // comes from — data-classification.md section 3 — is an argument about a card that is printed
        // and left on a bench, and reading it as "no view carries contact details" made the customer
        // record undeclarable and left it masking by hand instead. The surface is declared per view so
        // that the scope is a stated thing rather than an artefact of there being only workshop views.
        string[] required = ["CustomerContact", "CustomerNotes", "Pricing", "PaymentState"];

        var workshop = Document.Section("views").Rows
            .Where(row => string.Equals(row.Text("Surface"), nameof(ViewSurface.Workshop), StringComparison.Ordinal))
            .ToArray();

        workshop.Length.ShouldBeGreaterThan(0, "the document approves no workshop surface at all");

        foreach (var row in workshop)
        {
            var withheld = row.List("Withheld");
            required.Where(name => !withheld.Contains(name, StringComparer.Ordinal))
                .ShouldBeEmpty($"{row.Text("View")} does not withhold every workshop-forbidden class");
        }
    }

    [Fact]
    public void NoCounterSurfaceIsApprovedToCarryMeasurementsPricingOrPaymentState()
    {
        // The other half of the same decision. A counter surface may carry contact details — that is
        // what the counter is for — but a screen about a customer is still not the place for the
        // figures a garment is cut to, a price, a payment, or a member of staff's throughput.
        string[] required = ["Measurement", "Pricing", "PaymentState", "StaffPerformance"];

        var counter = Document.Section("views").Rows
            .Where(row => string.Equals(row.Text("Surface"), nameof(ViewSurface.Counter), StringComparison.Ordinal))
            .ToArray();

        counter.Length.ShouldBeGreaterThan(0, "the document approves no counter surface at all");

        foreach (var row in counter)
        {
            var withheld = row.List("Withheld");
            required.Where(name => !withheld.Contains(name, StringComparer.Ordinal))
                .ShouldBeEmpty($"{row.Text("View")} does not withhold every counter-forbidden class");
        }
    }

    [Fact]
    public void NoRoleIsShownAFieldOfAClassItsViewWithholds()
    {
        var offending =
            from view in Catalogue.All
            from role in SystemRoles.All
            from name in view.VisibleTo(role.Permissions.ToHashSet(StringComparer.Ordinal))
            let field = view.Field(name)!
            where (field.Classification & view.Withheld) != 0
            select $"{role.Key} sees {view.Key}.{name} ({field.Classification})";

        offending.ShouldBeEmpty();
    }

    private static string[] DerivedRoleRows() =>
    [
        .. from view in Catalogue.All.OrderBy(view => view.Key, StringComparer.Ordinal)
           from role in SystemRoles.All
           let permissions = role.Permissions.ToHashSet(StringComparer.Ordinal)
           let visible = view.VisibleTo(permissions)
           select Row(view.Key, role.Key, visible.Count > 0, visible),
    ];

    private static string Row(string view, string role, bool reaches, IReadOnlyList<string> visible)
        => $"{view} | {role} | {(reaches ? "yes" : "no")} | "
           + (visible.Count == 0 ? "—" : string.Join(", ", visible));

    private static string RenderExpected(IReadOnlyList<string> rows)
        => "the role table is derived, not written. Replace the role-fields block with:\n"
           + string.Join('\n', rows.Select(row => "| " + string.Join(
               " | ",
               row.Split(" | ").Select((cell, index) => index == 3 ? BackTickList(cell) : $"`{cell}`")) + " |"));

    private static string BackTickList(string cell)
        => cell == "—" ? "—" : string.Join(", ", cell.Split(", ").Select(item => $"`{item}`"));

    private static IReadOnlyList<string> WithheldNames(FieldClassification withheld)
        =>
        [
            .. Enum.GetValues<FieldClassification>()
                .Where(value => value != FieldClassification.None && withheld.HasFlag(value))
                .Select(value => value.ToString()),
        ];
}
