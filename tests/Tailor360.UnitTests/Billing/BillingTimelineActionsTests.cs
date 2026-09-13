using System.Reflection;
using Shouldly;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// Which of <see cref="InvoiceHandler"/>'s own actions reach a customer's timeline, and what seeing one
/// costs.
/// </summary>
/// <remarks>
/// Scoped to <see cref="InvoiceHandler"/> alone rather than to the whole Application assembly, unlike
/// <c>Customers.TimelineActionsTests</c>: Billing writes many kinds of thing — a tax configuration, a
/// price list, a payment mode — and only an invoice's own facts are customer history. Reflecting over
/// every <c>…Action</c> constant in the assembly would ask this map to excuse actions that were never
/// its business in the first place, such as a tax code being added to a draft configuration.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class BillingTimelineActionsTests
{
    [Fact]
    public void NamesEveryInvoiceActionOrRecordsItAsDeliberatelyAbsent()
    {
        var declared = InvoiceActionConstants();

        declared.Length.ShouldBe(7, "InvoiceHandler's own action constants were not found by reflection");

        var mapped = BillingTimelineActions.All.ToHashSet(StringComparer.Ordinal);
        var missing = declared
            .Where(action => !mapped.Contains(action))
            .Where(action => !DeliberatelyAbsent.Contains(action))
            .ToArray();

        missing.ShouldBeEmpty(
            "an invoice action is not on the timeline and is not recorded as deliberately absent:\n"
            + string.Join('\n', missing));
    }

    [Fact]
    public void RecordsOnlyDraftAndDiscardActionsAsDeliberatelyAbsent()
    {
        var declared = InvoiceActionConstants().ToHashSet(StringComparer.Ordinal);

        DeliberatelyAbsent.Where(action => !declared.Contains(action)).ShouldBeEmpty(
            "the absent list names an action InvoiceHandler does not declare, so it excuses nothing");

        DeliberatelyAbsent.Where(BillingTimelineActions.All.Contains).ShouldBeEmpty(
            "an action cannot be both mapped onto the timeline and recorded as absent from it");

        DeliberatelyAbsent.ShouldBe(
            [InvoiceHandler.DraftedAction, InvoiceHandler.UpdatedAction, InvoiceHandler.DiscardedAction],
            ignoreOrder: true,
            "OD-22: nothing about a draft is authoritative, so neither drafting nor re-pricing nor discarding one reaches a customer's history");
    }

    [Fact]
    public void PutsNothingOnTheTimelineThatInvoiceHandlerDoesNotWrite()
    {
        var declared = InvoiceActionConstants().ToHashSet(StringComparer.Ordinal);

        BillingTimelineActions.All.Where(action => !declared.Contains(action)).ShouldBeEmpty(
            "the timeline maps an action InvoiceHandler does not write, so it would never appear and the mapping is dead");
    }

    [Fact]
    public void ShowsEveryMappedActionOnlyToACallerHoldingCreateInvoice()
    {
        BillingTimelineActions.VisibleTo(Holding()).ShouldBeEmpty();
        BillingTimelineActions.VisibleTo(Holding("customers.read")).ShouldBeEmpty(
            "reading a customer is not reading her invoices — the two permissions gate different things");

        var visible = BillingTimelineActions.VisibleTo(Holding(BillingPermissions.CreateInvoice));
        visible.ShouldBe(BillingTimelineActions.All.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void GatesTheReasonOnTheCustomerNotesPermission()
    {
        BillingTimelineActions.ReasonPermission.ShouldBe(CustomersPermissions.ReadNotes);
    }

    [Fact]
    public void GivesEveryMappedActionATitleThatIsNotJustTheActionBack()
    {
        foreach (var action in BillingTimelineActions.All)
        {
            BillingTimelineActions.TitleOf(action).ShouldNotBe(
                action, $"{action} has no title, so a screen would print its dotted name at a customer");
        }
    }

    [Fact]
    public void AnswersTheActionItselfForSomethingItDoesNotKnow()
    {
        BillingTimelineActions.TitleOf("payments.record").ShouldBe("payments.record");
    }

    private static readonly HashSet<string> DeliberatelyAbsent = new(StringComparer.Ordinal)
    {
        InvoiceHandler.DraftedAction,
        InvoiceHandler.UpdatedAction,
        InvoiceHandler.DiscardedAction,
    };

    private static HashSet<string> Holding(params string[] permissions)
        => new(permissions, StringComparer.Ordinal);

    /// <summary>Every <c>…Action</c> constant declared directly on <see cref="InvoiceHandler"/>.</summary>
    private static string[] InvoiceActionConstants()
        =>
        [
            .. typeof(InvoiceHandler)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field is { IsLiteral: true, IsInitOnly: false })
                .Where(field => field.Name.EndsWith("Action", StringComparison.Ordinal))
                .Select(field => field.GetRawConstantValue() as string)
                .Where(value => value is { Length: > 0 })
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal),
        ];
}
