using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tailor360.Modules.Integration.Infrastructure.Documents;

/// <summary>
/// The layout of a payment receipt on the 80 mm roll (plan D15): the branch, the receipt's number, date and
/// barcode, the order and the mode with the reference as recorded, the amount, where it went by invoice
/// number, what is held as an advance, and what the order still owed at issue. Reads the model the Billing
/// module builds (<c>DocumentModels.Receipt</c> there names every key this reads). No customer detail is
/// printed: the receipt names the order.
/// </summary>
internal static class ReceiptTemplateLayout
{
    /// <summary>The roll's printable width.</summary>
    public const float WidthMillimetres = 80f;

    private const string Muted = "#555555";
    private const string Rule = "#BBBBBB";

    public static void Body(IContainer container, DocumentModel model)
    {
        var currency = model.Text("currency");
        container.Column(column =>
        {
            column.Spacing(3);

            column.Item().AlignCenter().Text(model.Text("branchName")).FontSize(11).SemiBold();
            column.Item().AlignCenter().Text("PAYMENT RECEIPT").FontSize(9).SemiBold();
            column.Item().AlignCenter().Text(model.Text("number")).FontSize(10).SemiBold();
            column.Item().AlignCenter().Text($"Date {model.Text("issuedOn")} · FY {model.Text("financialYear")}").FontColor(Muted);
            column.Item().LineHorizontal(0.5f).LineColor(Rule);

            Pair(column, "Order", model.Text("orderNumber"));
            Pair(column, "Mode", model.Text("modeName"));
            if (model.Text("reference").Length > 0)
            {
                Pair(column, "Reference", model.Text("reference"));
            }

            column.Item().LineHorizontal(0.5f).LineColor(Rule);
            Pair(column, "Received", DocumentModel.Money(model.Amount("amount"), currency), emphasis: true);

            var allocations = model.Sections("allocations");
            if (allocations.Count > 0)
            {
                column.Item().PaddingTop(2).Text("Applied to").FontColor(Muted).FontSize(7.5f);
                foreach (var allocation in allocations)
                {
                    Pair(column, allocation.Text("invoiceNumber"), DocumentModel.Money(allocation.Amount("amount"), currency));
                }
            }

            if (model.Amount("unappliedAdvance") != 0m)
            {
                Pair(column, "Held as advance", DocumentModel.Money(model.Amount("unappliedAdvance"), currency));
            }

            column.Item().LineHorizontal(0.5f).LineColor(Rule);
            Pair(column, "Order balance due", DocumentModel.Money(model.Amount("orderOutstanding"), currency), emphasis: true);

            var barcode = model.Text("barcodePayload");
            if (barcode.Length > 0)
            {
                column.Item().PaddingTop(4).AlignCenter().Width(180).Height(34).Svg(Code128BarcodeRenderer.Svg(barcode, 180, 34));
                column.Item().AlignCenter().Text(barcode).FontSize(7.5f).FontColor(Muted);
            }

            column.Item().PaddingTop(2).AlignCenter().Text("Thank you").FontSize(7.5f).FontColor(Muted);
        });
    }

    private static void Pair(ColumnDescriptor column, string label, string value, bool emphasis = false)
        => column.Item().Row(row =>
        {
            var left = row.RelativeItem().Text(label);
            var right = row.ConstantItem(90).AlignRight().Text(value);
            if (emphasis)
            {
                left.SemiBold();
                right.SemiBold();
            }
        });
}
