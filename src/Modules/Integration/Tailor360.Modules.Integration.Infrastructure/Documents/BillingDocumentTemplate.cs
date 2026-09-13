using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tailor360.Modules.Integration.Infrastructure.Documents;

/// <summary>
/// The layout of an invoice, a credit note and a debit note: the supplier and the customer, the document's
/// number, date and barcode, the lines with their rates, surcharges, discounts and tax components, the
/// totals and the balance, and the terms where the business has set them. Reads the model the Billing
/// module builds (<c>DocumentModels</c> there names every key this reads).
/// </summary>
internal static class BillingDocumentTemplate
{
    private const string Muted = "#555555";
    private const string Rule = "#BBBBBB";

    public static string TitleOf(string templateKey) => templateKey switch
    {
        QuestPdfRenderer.CreditNoteTemplate => "Credit note",
        QuestPdfRenderer.DebitNoteTemplate => "Debit note",
        QuestPdfRenderer.ReceiptTemplate => "Receipt",
        _ => "Tax invoice",
    };

    public static void Header(IContainer container, string templateKey, DocumentModel model)
    {
        var supplier = model.Section("supplier");
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(supplier.Text("tradeName")).FontSize(16).SemiBold();
                    left.Item().Text(supplier.Text("legalName")).FontColor(Muted);
                    left.Item().Text($"GSTIN {supplier.Text("gstin")} · State {supplier.Text("stateCode")}").FontColor(Muted);
                    left.Item().Text($"Branch {supplier.Text("branchName")}").FontColor(Muted);
                });
                row.ConstantItem(220).Column(right =>
                {
                    right.Item().AlignRight().Text(TitleOf(templateKey).ToUpperInvariant()).FontSize(14).SemiBold();
                    right.Item().AlignRight().Text(model.Text("number")).FontSize(12).SemiBold();
                    right.Item().AlignRight().Text($"Date {model.Text("issuedOn")} · FY {model.Text("financialYear")}").FontColor(Muted);
                    if (model.Flag("cancelled"))
                    {
                        right.Item().AlignRight().Text("CANCELLED").FontSize(12).SemiBold().FontColor(Colors.Red.Darken2);
                    }

                    var barcode = model.Text("barcodePayload");
                    if (barcode.Length > 0)
                    {
                        right.Item().PaddingTop(4).AlignRight().Width(200).Height(36).Svg(Code128BarcodeRenderer.Svg(barcode, 200, 36));
                        right.Item().AlignRight().Text(barcode).FontSize(8).FontColor(Muted);
                    }
                });
            });
            column.Item().PaddingTop(6).LineHorizontal(0.75f).LineColor(Rule);
        });
    }

    public static void Body(IContainer container, string templateKey, DocumentModel model)
    {
        var customer = model.Section("customer");
        var currency = model.Text("currency");
        var isNote = templateKey != QuestPdfRenderer.InvoiceTemplate;

        container.PaddingTop(8).Column(column =>
        {
            column.Spacing(8);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Billed to").FontColor(Muted).FontSize(8);
                    left.Item().Text(customer.Text("displayName")).SemiBold();
                    left.Item().Text($"Customer {customer.Text("customerNumber")}").FontColor(Muted);
                    foreach (var line in new[] { customer.Text("addressLine"), customer.Text("locality"), customer.Text("postcode") }.Where(line => line.Length > 0))
                    {
                        left.Item().Text(line);
                    }
                });
                row.RelativeItem().Column(right =>
                {
                    right.Item().Text(isNote ? "Against invoice" : "Order").FontColor(Muted).FontSize(8);
                    right.Item().Text(isNote ? model.Text("relatedNumber") : model.Text("orderNumber")).SemiBold();
                    right.Item().Text($"Place of supply {model.Text("placeOfSupplyStateCode")} · {model.Text("scheme")}").FontColor(Muted);
                    if (model.Text("reason").Length > 0)
                    {
                        right.Item().Text($"Reason: {model.Text("reason")}");
                    }
                });
            });

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(18);
                    columns.RelativeColumn(4);
                    columns.ConstantColumn(40);
                    columns.ConstantColumn(70);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    foreach (var (title, right) in new[] { ("#", false), ("Description", false), ("Qty", true), ("Rate", true), ("Taxable", true), ("Tax", true), ("Total", true) })
                    {
                        var cell = header.Cell().BorderBottom(0.75f).BorderColor(Rule).PaddingVertical(3);
                        (right ? cell.AlignRight() : cell).Text(title).SemiBold().FontSize(8);
                    }
                });

                foreach (var line in model.Sections("lines"))
                {
                    table.Cell().PaddingVertical(3).Text(line.Text("lineNumber"));
                    table.Cell().PaddingVertical(3).Column(description =>
                    {
                        description.Item().Text(line.Text("description"));
                        description.Item().Text($"{line.Text("itemCode")} · HSN/SAC {line.Text("classification")}").FontSize(7.5f).FontColor(Muted);
                        foreach (var surcharge in line.Sections("surcharges"))
                        {
                            description.Item().Text($"+ {surcharge.Text("description")} {DocumentModel.Money(surcharge.Amount("amount"), currency)}").FontSize(7.5f).FontColor(Muted);
                        }

                        if (line.Text("discountRuleCode").Length > 0)
                        {
                            description.Item().Text($"Discount {line.Text("discountRuleCode")} -{DocumentModel.Money(line.Amount("discountAmount"), currency)}").FontSize(7.5f).FontColor(Muted);
                        }

                        foreach (var tax in line.Sections("taxes"))
                        {
                            description.Item().Text($"{tax.Text("kind")} {tax.Text("ratePercent")}% {DocumentModel.Money(tax.Amount("amount"), currency)}").FontSize(7.5f).FontColor(Muted);
                        }
                    });
                    table.Cell().PaddingVertical(3).AlignRight().Text(line.Text("quantity"));
                    table.Cell().PaddingVertical(3).AlignRight().Text(line.Text("rate").Length > 0 ? DocumentModel.Money(line.Amount("rate"), currency) : string.Empty);
                    table.Cell().PaddingVertical(3).AlignRight().Text(DocumentModel.Money(line.Amount("taxableValue"), currency));
                    table.Cell().PaddingVertical(3).AlignRight().Text(DocumentModel.Money(line.Amount("taxTotal"), currency));
                    table.Cell().PaddingVertical(3).AlignRight().Text(DocumentModel.Money(line.Amount("lineTotal"), currency));
                }
            });

            var totals = model.Section("totals");
            column.Item().AlignRight().Width(260).Column(summary =>
            {
                void Line(string label, string key, bool emphasis = false)
                {
                    var amount = totals.Amount(key);
                    if (amount == 0m && !emphasis)
                    {
                        return;
                    }

                    summary.Item().Row(row =>
                    {
                        var left = row.RelativeItem().Text(label);
                        var right = row.ConstantItem(110).AlignRight().Text(DocumentModel.Money(amount, currency));
                        if (emphasis)
                        {
                            left.SemiBold();
                            right.SemiBold();
                        }
                    });
                }

                Line("Subtotal", "subtotal");
                Line("Discount", "discountTotal");
                Line("Taxable value", "taxableValue", emphasis: true);
                Line("CGST", "centralTax");
                Line("SGST", "stateTax");
                Line("IGST", "integratedTax");
                Line("Cess", "cess");
                Line("Round-off", "roundOff");
                summary.Item().PaddingTop(2).LineHorizontal(0.75f).LineColor(Rule);
                Line(isNote ? "Note total" : "Grand total", "grandTotal", emphasis: true);
                if (!isNote)
                {
                    Line("Balance due", "balanceDue", emphasis: true);
                }
            });

            var terms = model.Text("terms");
            if (terms.Length > 0)
            {
                column.Item().PaddingTop(6).Text(terms).FontSize(8).FontColor(Muted);
            }
        });
    }

    public static void Footer(IContainer container, DocumentModel model)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(0.5f).LineColor(Rule);
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"{model.Section("supplier").Text("legalName")} · GSTIN {model.Section("supplier").Text("gstin")}").FontSize(7.5f).FontColor(Muted);
                row.ConstantItem(80).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(Muted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }
}
