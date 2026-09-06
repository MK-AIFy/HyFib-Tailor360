using Shouldly;
using Tailor360.Platform.Security.FieldVisibility;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// Field-level minimisation: what a response may carry, and what one caller is shown of it.
/// </summary>
/// <remarks>
/// The tests are written against the declared views rather than against a handler, because the whole
/// design is that no handler decides this. A test that projected a payload by hand would be testing the
/// thing the mechanism exists to remove.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FieldVisibilityTests
{
    private static readonly ResponseViewCatalogue Catalogue = new([new ApplicationResponseViews()]);

    [Fact]
    public void DeclaresTheThreeSurfacesTheWorkshopReads()
    {
        Catalogue.All.Select(view => view.Key).ShouldBe(
            [
                CustomersResponseViews.MeasurementSheet,
                OrdersResponseViews.JobCard,
                OrdersResponseViews.WorkQueue,
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void RefusesToBuildAViewThatCarriesAClassItWithholds()
    {
        // This is the invariant that makes "the job card has no price on it" a property of the system
        // rather than an observation about today's code.
        var build = () => new ResponseView(
            "orders.job_card_with_a_price",
            PermissionModules.Orders,
            "A job card somebody added a total to.",
            OrdersPermissions.Read,
            FieldClassification.Pricing,
            [
                new ViewField("jobNumber", FieldClassification.Operational, null, "The number."),
                new ViewField("totalAmount", FieldClassification.Pricing, null, "The total."),
            ]);

        var error = build.ShouldThrow<ArgumentException>();
        error.Message.ShouldContain("totalAmount");
    }

    [Fact]
    public void RefusesAViewThatDeclaresAFieldTwice()
    {
        var build = () => new ResponseView(
            "orders.duplicate",
            PermissionModules.Orders,
            "A view with a repeated field.",
            OrdersPermissions.Read,
            FieldClassification.None,
            [
                new ViewField("jobNumber", FieldClassification.Operational, null, "The number."),
                new ViewField("jobNumber", FieldClassification.Operational, null, "The number again."),
            ]);

        build.ShouldThrow<ArgumentException>().Message.ShouldContain("more than once");
    }

    [Fact]
    public void RefusesTwoViewsClaimingOneKey()
    {
        var build = () => new ResponseViewCatalogue(
            [new ApplicationResponseViews(), new ApplicationResponseViews()]);

        build.ShouldThrow<InvalidOperationException>().Message.ShouldContain("more than one module");
    }

    [Fact]
    public void NoWorkshopSurfaceCanCarryContactPricingOrPaymentState()
    {
        string[] workshopSurfaces =
            [OrdersResponseViews.JobCard, OrdersResponseViews.WorkQueue, CustomersResponseViews.MeasurementSheet];

        foreach (var key in workshopSurfaces)
        {
            var view = Catalogue.Require(key);

            FieldClassification[] forbidden =
            [
                FieldClassification.CustomerContact,
                FieldClassification.CustomerNotes,
                FieldClassification.Pricing,
                FieldClassification.PaymentState,
            ];

            foreach (var classification in forbidden)
            {
                view.Withheld.HasFlag(classification).ShouldBeTrue(
                    $"{view.Key} does not withhold {classification}");
                view.Fields.ShouldAllBe(field => (field.Classification & classification) == 0);
            }
        }
    }

    [Fact]
    public void EveryFieldPermissionAndEveryViewPermissionIsInTheCatalogue()
    {
        var permissions = new PermissionCatalogue([new ApplicationPermissions()]);
        var unknown = new List<string>();

        foreach (var view in Catalogue.All)
        {
            if (!permissions.Contains(view.RequiredPermission))
            {
                unknown.Add($"{view.Key} requires '{view.RequiredPermission}'");
            }

            unknown.AddRange(
                from field in view.Fields
                where field.RequiredPermission is not null && !permissions.Contains(field.RequiredPermission)
                select $"{view.Key}.{field.Name} requires '{field.RequiredPermission}'");
        }

        unknown.ShouldBeEmpty(
            "a view or a field names a permission the catalogue does not declare, which would hide the "
            + "field from everybody for ever:\n" + string.Join('\n', unknown));
    }

    [Fact]
    public void ShowsTheTailorTheWholeJobCardExceptWhatIsGatedElsewhere()
    {
        // A Tailor holds neither customers.read nor customers.read_contact. The customer's name is on
        // the card all the same, because the name is what makes a job card usable and the contact
        // details are what would make it dangerous.
        var mask = Mask(
            OrdersResponseViews.JobCard,
            OrdersPermissions.Read,
            CustomersPermissions.ReadMeasurementSheet,
            MediaPermissions.Read);

        mask.Allows("customerName").ShouldBeTrue();
        mask.Allows("measurements").ShouldBeTrue();
        mask.Allows("referenceImages").ShouldBeTrue();
        mask.View.Fields.ShouldAllBe(field => mask.Allows(field.Name));
    }

    [Fact]
    public void WithholdsTheMeasurementsFromACallerWhoMayOpenTheJobButNotTheSheet()
    {
        var mask = Mask(OrdersResponseViews.JobCard, OrdersPermissions.Read);

        mask.Allows("jobNumber").ShouldBeTrue();
        mask.Allows("customerName").ShouldBeTrue();
        mask.Allows("measurements").ShouldBeFalse();
        mask.Allows("referenceImages").ShouldBeFalse();
    }

    [Fact]
    public void ShowsNothingAtAllToACallerWhoCannotReachTheView()
    {
        var mask = Mask(OrdersResponseViews.JobCard, CustomersPermissions.ReadMeasurementSheet);

        mask.IsEmpty.ShouldBeTrue();
        mask.Allows("jobNumber").ShouldBeFalse();
    }

    [Fact]
    public void KeepsThroughputFiguresOffTheQueueForSomebodyWithoutTheReportingPermission()
    {
        var tailor = Mask(OrdersResponseViews.WorkQueue, OrdersPermissions.Read);
        var lead = Mask(OrdersResponseViews.WorkQueue, OrdersPermissions.Read, ReportingPermissions.Read);

        tailor.Allows("assigneeThroughput").ShouldBeFalse();
        lead.Allows("assigneeThroughput").ShouldBeTrue();
    }

    [Fact]
    public void DropsAMaskedFieldFromTheBodyWithoutTheHandlerHavingToKnow()
    {
        var payload = Mask(OrdersResponseViews.JobCard, OrdersPermissions.Read)
            .Build()
            .Set("jobNumber", "J-CBE01-2627-000512-01")
            .Set("customerName", "Synthetic Customer")
            .Set("measurements", new { Bust = 900 })
            .ToPayload();

        payload.Keys.ShouldBe(["jobNumber", "customerName"]);
        payload["jobNumber"].ShouldBe("J-CBE01-2627-000512-01");
    }

    [Fact]
    public void ReturnsFieldsInTheViewsDeclaredOrderWhateverOrderTheHandlerSetThem()
    {
        var payload = Mask(OrdersResponseViews.JobCard, OrdersPermissions.Read)
            .Build()
            .Set("customerName", "Synthetic Customer")
            .Set("jobNumber", "J-CBE01-2627-000512-01")
            .ToPayload();

        payload.Keys.ShouldBe(["jobNumber", "customerName"]);
    }

    [Fact]
    public void RefusesAFieldTheViewDoesNotDeclare()
    {
        // Not a refusal of the caller: a refusal of the field. Dropping it silently would let an
        // unapproved value ship the day somebody spells it correctly.
        var build = () => Mask(OrdersResponseViews.JobCard, OrdersPermissions.Read)
            .Build()
            .Set("customerPhone", "not on a job card");

        build.ShouldThrow<InvalidOperationException>().Message.ShouldContain("customerPhone");
    }

    [Fact]
    public void NamesTheViewsItKnowsWhenAskedForOneItDoesNot()
    {
        var ask = () => Catalogue.Require("orders.imaginary");

        ask.ShouldThrow<InvalidOperationException>().Message.ShouldContain(OrdersResponseViews.JobCard);
    }

    private static FieldMask Mask(string viewKey, params string[] permissions)
    {
        var policy = new FieldVisibilityPolicy(Catalogue, new PermissionlessUser());
        return policy.MaskFor(viewKey, new HashSet<string>(permissions, StringComparer.Ordinal));
    }

    /// <summary>
    /// A caller holding nothing. The masks under test are computed from an explicit permission set, so
    /// this double exists only to satisfy the policy's constructor — and its emptiness is the assurance
    /// that no test here passes because of an ambient caller.
    /// </summary>
    private sealed class PermissionlessUser : Tailor360.Platform.Security.Authorisation.ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid UserId => Guid.Empty;

        public string PrincipalId => string.Empty;

        public string DisplayName => string.Empty;

        public Tailor360.Platform.Abstractions.Multitenancy.OrganisationContext Context { get; } =
            new(Guid.Empty, null);

        public IReadOnlySet<Guid> AssignedBranches { get; } = new HashSet<Guid>();

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(StringComparer.Ordinal);

        public bool MfaSatisfied => false;

        public bool IsSignInComplete => false;

        public DateTimeOffset? LastReauthenticatedAt => null;

        public bool HasPermission(string permissionKey) => false;

        public bool CanActInBranch(Guid branchId) => false;
    }
}
