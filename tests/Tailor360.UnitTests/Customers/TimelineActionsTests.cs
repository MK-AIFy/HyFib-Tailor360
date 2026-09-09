using System.Reflection;
using Shouldly;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Application.Preferences;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// Which of this module's audited actions reach a customer's timeline, and what seeing one costs.
/// </summary>
/// <remarks>
/// The map is the security decision of the timeline slice. Everything else about the timeline is
/// merging and paging; this is the part that decides whether somebody who may open a customer record
/// is thereby told that her consent was withdrawn or that her whole file was exported last Tuesday.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TimelineActionsTests
{
    [Fact]
    public void NamesEveryActionThisModuleWritesAgainstACustomer()
    {
        // The list is closed, so the risk is the opposite of the usual one: not that something
        // unapproved is on the timeline, but that an audited action was added later and quietly never
        // appeared. This finds every action constant the module declares and insists each is either
        // mapped or deliberately absent.
        var declared = ActionConstants();

        declared.Length.ShouldBeGreaterThan(10, "the module's audited actions were not found by reflection");

        var mapped = TimelineActions.All.ToHashSet(StringComparer.Ordinal);
        var missing = declared
            .Where(action => !mapped.Contains(action))
            .Where(action => !DeliberatelyAbsent.Contains(action))
            .ToArray();

        missing.ShouldBeEmpty(
            "an audited action is not on the timeline and is not recorded as deliberately absent:\n"
            + string.Join('\n', missing));
    }

    [Fact]
    public void RecordsOnlyActionsThatAreNotAboutOneCustomerAsDeliberatelyAbsent()
    {
        // The absent list is the escape hatch of the test above, so it needs a guard of its own: every entry has
        // to be an action this module really declares. An entry for an action that no longer exists would sit
        // there excusing nothing, and would go on excusing a future action that happened to be named the same.
        var declared = ActionConstants().ToHashSet(StringComparer.Ordinal);

        DeliberatelyAbsent.Where(action => !declared.Contains(action)).ShouldBeEmpty(
            "the absent list names an action nothing declares, so it excuses nothing");

        DeliberatelyAbsent.Where(TimelineActions.All.Contains).ShouldBeEmpty(
            "an action cannot be both mapped onto the timeline and recorded as absent from it");
    }

    [Fact]
    public void PutsNothingOnTheTimelineThatTheModuleDoesNotWrite()
    {
        var declared = ActionConstants().ToHashSet(StringComparer.Ordinal);

        TimelineActions.All.Where(action => !declared.Contains(action)).ShouldBeEmpty(
            "the timeline maps an action nothing writes, so it would never appear and the mapping is dead");
    }

    [Fact]
    public void WritesItsEntriesAgainstTheEntityTypeTheAuditHelpersUse()
    {
        // The constant is repeated because the audit helpers are internal to the Application project.
        // A mistyped copy would not fail: it would read an empty trail and show an empty timeline,
        // which looks exactly like a customer nothing has happened to.
        var auditEntityTypes = typeof(CustomerHandler).Assembly.GetTypes()
            .Where(type => type.Name.EndsWith("Audit", StringComparison.Ordinal))
            .Select(type => type.GetField("EntityType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(field => field is not null)
            .Select(field => field!.GetRawConstantValue() as string)
            .Where(value => value is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        auditEntityTypes.ShouldNotBeEmpty("no audit helper declared an entity type");

        // Two, and only two. Everything this module audits is either about one customer — which is what the
        // timeline reads — or about a measurement template, which is configuration and belongs to no customer.
        // Listing them exactly is what keeps the guard: a typo in either produces a third value and fails here
        // rather than silently reading an empty trail, and a genuinely new kind of audited thing has to be
        // thought about rather than appearing by accident.
        string[] known = [TimelineActions.EntityType, MeasurementTemplateAudit.EntityType];

        auditEntityTypes.Order(StringComparer.Ordinal)
            .ShouldBe(known.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ShowsTheRecordsOwnHistoryToAnybodyWhoMayOpenTheRecord()
    {
        var visible = TimelineActions.VisibleTo(Holding(CustomersPermissions.Read));

        visible.ShouldContain(CustomerHandler.RegisteredAction);
        visible.ShouldContain(CustomerHandler.CorrectedAction);
        visible.ShouldContain(CustomerHandler.DeactivatedAction);
        visible.ShouldContain(CustomerHandler.MergedAction);
        visible.ShouldContain(CustomerHandler.OpenedAtBranchAction);
    }

    [Fact]
    public void WithholdsConsentAndPreferenceEntriesWithoutReadConsent()
    {
        // Withheld whole, not blanked. "Consent withdrawn" is itself the fact customers.read_consent
        // gates, so an entry with its detail removed would still disclose it.
        var without = TimelineActions.VisibleTo(Holding(CustomersPermissions.Read));
        var with = TimelineActions.VisibleTo(
            Holding(CustomersPermissions.Read, CustomersPermissions.ReadConsent));

        without.ShouldNotContain(ConsentHandler.RecordedAction);
        without.ShouldNotContain(ConsentHandler.WithdrawnAction);
        without.ShouldNotContain(PreferenceHandler.ChangedAction);

        with.ShouldContain(ConsentHandler.RecordedAction);
        with.ShouldContain(ConsentHandler.WithdrawnAction);
        with.ShouldContain(PreferenceHandler.ChangedAction);
    }

    [Fact]
    public void WithholdsSubjectAccessExportEntriesWithoutTheExportPermission()
    {
        var without = TimelineActions.VisibleTo(Holding(CustomersPermissions.Read));
        var with = TimelineActions.VisibleTo(
            Holding(CustomersPermissions.Read, CustomersPermissions.Export));

        without.ShouldNotContain(CustomerExportHandler.GeneratedAction);
        without.ShouldNotContain(CustomerExportHandler.DownloadedAction);
        without.ShouldNotContain(CustomerExportHandler.PurgedAction);

        with.ShouldContain(CustomerExportHandler.GeneratedAction);
        with.ShouldContain(CustomerExportHandler.DownloadedAction);
        with.ShouldContain(CustomerExportHandler.PurgedAction);
    }

    [Fact]
    public void ShowsNothingToACallerHoldingNothing()
    {
        TimelineActions.VisibleTo(Holding()).ShouldNotBeEmpty(
            "the record's own entries carry no permission of their own — the view's is the gate, and "
            + "the endpoint demands it, so this map is not where a caller with nothing is refused");

        TimelineActions.VisibleTo(Holding()).ShouldNotContain(ConsentHandler.RecordedAction);
    }

    [Fact]
    public void GatesTheReasonOnTheNotesPermission()
    {
        TimelineActions.ReasonPermission.ShouldBe(CustomersPermissions.ReadNotes);
    }

    [Fact]
    public void GivesEveryMappedActionATitleThatIsNotJustTheActionBack()
    {
        foreach (var action in TimelineActions.All)
        {
            TimelineActions.TitleOf(action).ShouldNotBe(
                action, $"{action} has no title, so a screen would print its dotted name at a customer");
        }
    }

    [Fact]
    public void AnswersTheActionItselfForSomethingItDoesNotKnow()
    {
        // A source only ever asks about actions it has already filtered to the mapped set, so this is
        // the answer to a question that should not be asked. It is a legible string rather than a
        // throw, because failing a whole timeline over one unrecognised row would be the worse trade.
        TimelineActions.TitleOf("orders.order.confirmed").ShouldBe("orders.order.confirmed");
        TimelineActions.PermissionFor("orders.order.confirmed").ShouldBeNull();
    }

    /// <summary>
    /// Audited actions that are deliberately not on any customer's timeline.
    /// </summary>
    /// <remarks>
    /// Administering a measurement template is configuration, not something that happened to a customer. "The
    /// Owner published version 3 of the blouse template" is true of the shop, not of Mrs Devi — putting it on her
    /// timeline would say something about her that is not about her, and would say the same thing on every
    /// customer in the shop at once. The entries are audited, and they are read through the template's own trail.
    /// </remarks>
    private static readonly HashSet<string> DeliberatelyAbsent = new(StringComparer.Ordinal)
    {
        MeasurementTemplateHandler.TemplateCreatedAction,
        MeasurementTemplateHandler.DraftedAction,
        MeasurementTemplateHandler.ClonedAction,
        MeasurementTemplateHandler.FieldAddedAction,
        MeasurementTemplateHandler.FieldChangedAction,
        MeasurementTemplateHandler.FieldRemovedAction,
        MeasurementTemplateHandler.SubmittedAction,
        MeasurementTemplateHandler.ReturnedAction,
        MeasurementTemplateHandler.ApprovedAction,
        MeasurementTemplateHandler.PublishedAction,
        MeasurementTemplateHandler.RetiredAction,
    };

    private static HashSet<string> Holding(params string[] permissions)
        => new(permissions, StringComparer.Ordinal);

    /// <summary>Every <c>…Action</c> constant the Application project declares.</summary>
    private static string[] ActionConstants()
        =>
        [
            .. typeof(CustomerHandler).Assembly.GetTypes()
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
                .Where(field => field is { IsLiteral: true, IsInitOnly: false })
                .Where(field => field.Name.EndsWith("Action", StringComparison.Ordinal))
                .Select(field => field.GetRawConstantValue() as string)
                .Where(value => value is { Length: > 0 })
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal),
        ];
}
