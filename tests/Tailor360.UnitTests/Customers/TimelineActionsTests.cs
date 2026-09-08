using System.Reflection;
using Shouldly;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Application.Customers;
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
        var missing = declared.Where(action => !mapped.Contains(action)).ToArray();

        missing.ShouldBeEmpty(
            "an audited action is not on the timeline and is not recorded as deliberately absent:\n"
            + string.Join('\n', missing));
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
        auditEntityTypes.ShouldAllBe(type => type == TimelineActions.EntityType);
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
