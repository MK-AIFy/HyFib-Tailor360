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
    public void DeclaresTheSixSurfacesTheApplicationServes()
    {
        Catalogue.All.Select(view => view.Key).ShouldBe(
            [
                CustomersResponseViews.MeasurementSheet,
                CustomersResponseViews.Record,
                CustomersResponseViews.SearchCard,
                CustomersResponseViews.Timeline,
                OrdersResponseViews.JobCard,
                OrdersResponseViews.WorkQueue,
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void SaysOfEveryViewWhoReadsIt()
    {
        // The surface is what decides the withheld set, so a view that did not say which it was would
        // be a view nobody had decided the rule for.
        Catalogue.All
            .Where(view => view.Surface is ViewSurface.Counter)
            .Select(view => view.Key)
            .ShouldBe(
                [
                    CustomersResponseViews.Record,
                    CustomersResponseViews.SearchCard,
                    CustomersResponseViews.Timeline,
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
            ViewSurface.Workshop,
            "A job card somebody added a total to.",
            OrdersPermissions.Read,
            SurfaceRules.ForbiddenOnAWorkshopSurface,
            [
                new ViewField("jobNumber", FieldClassification.Operational, null, "The number."),
                new ViewField("totalAmount", FieldClassification.Pricing, null, "The total."),
            ]);

        var error = build.ShouldThrow<ArgumentException>();
        error.Message.ShouldContain("totalAmount");
    }

    [Fact]
    public void RefusesAWorkshopViewThatWithholdsLessThanTheWorkshopForbids()
    {
        // The rule the ViewSurface column exists to make enforceable in both directions. Before it, a
        // fourth workshop view could be declared with a quietly shorter withheld list and only a
        // document test would have noticed.
        var build = () => new ResponseView(
            "orders.job_card_that_forgot",
            PermissionModules.Orders,
            ViewSurface.Workshop,
            "A job card whose author left CustomerContact out of the withheld set.",
            OrdersPermissions.Read,
            FieldClassification.CustomerNotes | FieldClassification.Pricing
            | FieldClassification.PaymentState,
            [new ViewField("jobNumber", FieldClassification.Operational, null, "The number.")]);

        build.ShouldThrow<ArgumentException>().Message.ShouldContain("CustomerContact");
    }

    [Fact]
    public void RefusesACounterViewThatWithholdsLessThanTheCounterForbids()
    {
        var build = () => new ResponseView(
            "customers.record_with_measurements",
            PermissionModules.Customers,
            ViewSurface.Counter,
            "A customer record somebody added the figures to.",
            CustomersPermissions.Read,
            FieldClassification.Media | FieldClassification.Pricing
            | FieldClassification.PaymentState | FieldClassification.StaffPerformance,
            [new ViewField("customerId", FieldClassification.Operational, null, "The record.")]);

        build.ShouldThrow<ArgumentException>().Message.ShouldContain("Measurement");
    }

    [Fact]
    public void TheTwoModuleConstantsAreTheSurfaceRulesAndNotSecondCopiesOfThem()
    {
        OrdersResponseViews.WithheldFromTheWorkshop
            .ShouldBe(SurfaceRules.ForbiddenOn(ViewSurface.Workshop));
        CustomersResponseViews.WithheldFromACustomerScreen
            .ShouldBe(SurfaceRules.ForbiddenOn(ViewSurface.Counter));
    }

    [Fact]
    public void GivesACommandTheFieldGatesWithoutTheViewsOwnGate()
    {
        // POST /customers demands customers.create, not customers.read. An ordinary mask would answer
        // a successful registration with an empty body; the reached mask answers with the record and
        // still withholds the contact block.
        var policy = new FieldVisibilityPolicy(
            Catalogue,
            new StatedUser(CustomersPermissions.Create));

        var mask = policy.MaskForReached(CustomersResponseViews.Record, CustomersPermissions.Create);

        mask.IsEmpty.ShouldBeFalse();
        mask.Allows("displayName").ShouldBeTrue();
        mask.Allows("phone").ShouldBeFalse();
    }

    [Fact]
    public void RefusesToStandInAPermissionTheCallerDoesNotHold()
    {
        // The property that stops MaskForReached being a way to grant yourself reach: the permission
        // named must be one the caller actually holds, which is to say the one the endpoint demanded.
        var policy = new FieldVisibilityPolicy(Catalogue, new StatedUser(CustomersPermissions.ReadContact));

        var ask = () => policy.MaskForReached(CustomersResponseViews.Record, CustomersPermissions.Create);

        ask.ShouldThrow<InvalidOperationException>().Message.ShouldContain(CustomersPermissions.Create);
    }

    [Fact]
    public void RefusesAViewThatDeclaresAFieldTwice()
    {
        var build = () => new ResponseView(
            "orders.duplicate",
            PermissionModules.Orders,
            ViewSurface.Workshop,
            "A view with a repeated field.",
            OrdersPermissions.Read,
            SurfaceRules.ForbiddenOnAWorkshopSurface,
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
        // Read off the declared surface rather than a list written here, so that a fourth workshop
        // view cannot be added and quietly left out of the rule it exists under.
        var workshopSurfaces = Catalogue.All.Where(view => view.Surface is ViewSurface.Workshop).ToArray();

        workshopSurfaces.Length.ShouldBe(3, "the workshop surfaces are the job card, the work queue and the sheet");

        foreach (var view in workshopSurfaces)
        {
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
    public void ShowsTheCounterAContactNumberAndTheWorkshopNone()
    {
        // The one behaviour the surface distinction exists for, asserted from both sides.
        var reception = Mask(
            CustomersResponseViews.Record,
            CustomersPermissions.Read,
            CustomersPermissions.ReadContact);

        var administrator = Mask(CustomersResponseViews.Record, CustomersPermissions.Read);

        reception.Allows("phone").ShouldBeTrue();
        reception.Allows("email").ShouldBeTrue();
        reception.Allows("addressLine").ShouldBeTrue();

        administrator.Allows("displayName").ShouldBeTrue();
        administrator.Allows("phone").ShouldBeFalse();
        administrator.Allows("email").ShouldBeFalse();
        administrator.Allows("addressLine").ShouldBeFalse();

        // And the discriminator that keeps the two answers apart on the wire is never itself withheld.
        administrator.Allows("contactIncluded").ShouldBeTrue();
    }

    [Fact]
    public void ShowsNothingOfACustomerToACallerWithoutCustomersRead()
    {
        var tailor = Mask(CustomersResponseViews.Record, CustomersPermissions.ReadContact);

        tailor.IsEmpty.ShouldBeTrue(
            "customers.read_contact opens no record on its own; it only widens one already opened");
    }

    [Fact]
    public void GatesEveryContactFieldOnTheOnePermissionAndNoOther()
    {
        // CustomerPayload.From asks the view which fields are contact and treats them as one decision.
        // That is only sound while they share a permission, so the assumption is asserted rather than
        // trusted.
        var contact = Catalogue.Require(CustomersResponseViews.Record).Fields
            .Where(field => field.Classification.HasFlag(FieldClassification.CustomerContact))
            .ToArray();

        contact.Select(field => field.Name).ShouldBe(
            ["phone", "alternatePhone", "email", "addressLine", "locality", "postcode"]);

        contact.ShouldAllBe(field => field.RequiredPermission == CustomersPermissions.ReadContact);
    }

    [Fact]
    public void ShowsEverySearchCardFieldToAnybodyWhoMaySearchAtAll()
    {
        // A card is masked by construction rather than by permission: the number it carries has had
        // everything but its last four digits replaced before it reaches the Api layer. Gating it
        // would empty the cross-branch disambiguation the search exists for.
        var mask = Mask(CustomersResponseViews.SearchCard, CustomersPermissions.Read);

        mask.View.Fields.ShouldAllBe(field => mask.Allows(field.Name));
        mask.Allows("maskedPhone").ShouldBeTrue();
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

    /// <summary>A caller holding exactly the permissions named, for the ambient-caller overloads.</summary>
    private sealed class StatedUser(params string[] permissions) : PermissionlessUser
    {
        public override IReadOnlySet<string> Permissions { get; } =
            new HashSet<string>(permissions, StringComparer.Ordinal);

        public override bool HasPermission(string permissionKey) => Permissions.Contains(permissionKey);
    }

    /// <summary>
    /// A caller holding nothing. The masks under test are computed from an explicit permission set, so
    /// this double exists only to satisfy the policy's constructor — and its emptiness is the assurance
    /// that no test here passes because of an ambient caller.
    /// </summary>
    private class PermissionlessUser : Tailor360.Platform.Security.Authorisation.ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid UserId => Guid.Empty;

        public string PrincipalId => string.Empty;

        public string DisplayName => string.Empty;

        public Tailor360.Platform.Abstractions.Multitenancy.OrganisationContext Context { get; } =
            new(Guid.Empty, null);

        public IReadOnlySet<Guid> AssignedBranches { get; } = new HashSet<Guid>();

        public virtual IReadOnlySet<string> Permissions { get; } = new HashSet<string>(StringComparer.Ordinal);

        public bool MfaSatisfied => false;

        public bool IsSignInComplete => false;

        public DateTimeOffset? LastReauthenticatedAt => null;

        public virtual bool HasPermission(string permissionKey) => false;

        public bool CanActInBranch(Guid branchId) => false;
    }
}
