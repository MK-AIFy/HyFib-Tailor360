using System.Text.Json.Nodes;

namespace Tailor360.Web.OpenApi;

/// <summary>
/// One request example per operation that takes a body, keyed by operation identifier.
/// </summary>
/// <remarks>
/// <para>
/// An example is the difference between a schema a reader can parse and a request a reader can send. It
/// is also what makes a generated client's tests and a reviewer's <c>curl</c> agree with the server, so
/// the lint fails an operation that accepts a body and offers no example.
/// </para>
/// <para>
/// They are keyed by operation identifier rather than by payload type on purpose: the examples are part
/// of the published documentation, which the backend-for-frontend owns, and keying on the type would
/// oblige the host to name a payload class from every module it composes.
/// </para>
/// <para>
/// <b>Every value here is invented.</b> Nothing in this file is a real address, a real name, a real
/// credential or a real device response, and nothing in it may become one: the file is published in the
/// API document and committed to the repository.
/// </para>
/// </remarks>
public static class PayloadExamples
{
    private static readonly Dictionary<string, string> Examples = new(StringComparer.Ordinal)
    {
        ["CreateInvoiceDraft"] = """
            {
              "orderId": "0199c2f0-0000-7000-8000-0000000000c1",
              "calculationReference": "order:0199c2f0-0000-7000-8000-0000000000c1:1",
              "garmentJobIds": null,
              "reason": null
            }
            """,

        ["RepriceInvoiceDraft"] = """
            {
              "on": "2027-04-05",
              "placeOfSupplyStateCode": "33",
              "lines": [
                {
                  "lineKey": "0199c2f0-0000-7000-8000-0000000000d1",
                  "itemCode": "BLOUSE_PATTERN_STITCHING",
                  "quantity": 1,
                  "surchargeItemCodes": ["PI_KATORI_CUP_LINING"],
                  "discount": { "ruleCode": "FESTIVAL", "value": 5, "reason": null },
                  "override": null
                }
              ],
              "reason": "The piping finish was dropped at the counter."
            }
            """,

        ["DiscardInvoiceDraft"] = """
            {
              "reason": "Drafted against the wrong order."
            }
            """,

        ["PostInvoice"] = """
            {
              "reason": null
            }
            """,

        ["DescribePaymentMode"] = """
            {
              "name": "Card (terminal)",
              "requiresReference": true,
              "requiresProvider": false,
              "allowedForRefund": false,
              "isActive": true,
              "branchIds": []
            }
            """,

        ["OpenCashierSession"] = """
            {
              "openingFloat": 2000.00
            }
            """,

        ["RecordPayment"] = """
            {
              "orderId": "019bd6b0-1111-7c3a-9d5e-2f4a6b8c0d1e",
              "modeCode": "UPI",
              "amount": 1134.00,
              "reference": "UPI-426114-8QX2"
            }
            """,

        ["AllocateAdvance"] = """
            {
              "invoiceId": "019bd6b0-2222-7e4b-8f6a-3a5b7c9d1e2f",
              "amount": 500.00,
              "reason": "The customer asked for the advance to go against the second invoice first."
            }
            """,

        ["CloseCashierSession"] = """
            {
              "denominations": [
                { "denomination": 500, "quantity": 3 },
                { "denomination": 200, "quantity": 2 },
                { "denomination": 100, "quantity": 1 }
              ],
              "modeTotals": [
                { "modeCode": "CARD", "counted": 4350.00 },
                { "modeCode": "UPI", "counted": 1200.00 }
              ],
              "reason": null
            }
            """,

        ["CancelInvoice"] = """
            {
              "reason": "Issued to the wrong customer; re-invoiced as INV-MAIN-2627-000012."
            }
            """,

        ["PostCreditNote"] = """
            {
              "lines": [
                { "garmentJobId": "0199c000-0000-7000-8000-000000000031", "taxableValue": 90.00 }
              ],
              "reason": "Lining charged twice."
            }
            """,

        ["PrintInvoice"] = """
            {
              "copies": 1
            }
            """,

        ["PostDebitNote"] = """
            {
              "lines": [
                { "garmentJobId": "0199c000-0000-7000-8000-000000000031", "taxableValue": 50.00 }
              ],
              "reason": "Express finishing agreed at collection."
            }
            """,

        ["PreviewPricing"] = """
            {
              "priceListVersionId": "0199c2f0-0000-7000-8000-0000000000e4",
              "taxConfigurationVersionId": null,
              "branchId": "0199c2f0-0000-7000-8000-0000000000a1",
              "on": "2027-04-05",
              "placeOfSupplyStateCode": "33",
              "lines": [
                {
                  "lineKey": "garment-1",
                  "itemCode": "BLOUSE_PATTERN_STITCHING",
                  "quantity": 1,
                  "surchargeItemCodes": ["PI_KATORI_CUP_LINING", "PI_PIPING_FINISH"],
                  "discount": null,
                  "override": null
                },
                {
                  "lineKey": "garment-2",
                  "itemCode": "BLOUSE_AARI_STITCHING",
                  "quantity": 1,
                  "surchargeItemCodes": [],
                  "discount": { "ruleCode": "FESTIVAL", "value": 5, "reason": null },
                  "override": { "rate": 580, "reason": "Quoted at the sample rate before the revision." }
                }
              ]
            }
            """,

        ["CreatePriceList"] = """
            {
              "code": "PL_CBE01",
              "name": "Coimbatore price list",
              "reason": null
            }
            """,

        ["RenamePriceList"] = """
            {
              "name": "Coimbatore and Tiruppur price list",
              "reason": "The Tiruppur branch prices from the same list from April."
            }
            """,

        ["CreatePriceListDraft"] = """
            {
              "name": "Rates from 1 April 2027",
              "notes": "Cloned from version 4; stitching up by 5%.",
              "effectiveFrom": "2027-04-01",
              "taxInclusive": false,
              "roundOff": "NearestRupee",
              "overrideThresholdPercent": 10,
              "branchIds": ["0199c2f0-0000-7000-8000-0000000000a1"],
              "cloneFromVersionId": "0199c2f0-0000-7000-8000-0000000000e4",
              "reason": null
            }
            """,

        ["DescribePriceListVersion"] = """
            {
              "name": "Rates from 1 April 2027",
              "notes": "Stitching up by 5%.",
              "effectiveFrom": "2027-04-01",
              "taxInclusive": false,
              "roundOff": "NearestRupee",
              "overrideThresholdPercent": 10,
              "branchIds": ["0199c2f0-0000-7000-8000-0000000000a1"],
              "cloneFromVersionId": null,
              "reason": "The effective date moved to the start of the financial year."
            }
            """,

        ["AddPriceListItem"] = """
            {
              "code": "BLOUSE_PATTERN_STITCHING",
              "description": "Blouse stitching, pattern work",
              "kind": "Service",
              "baseRate": 450,
              "unit": "each",
              "taxCode": "STITCHING_5",
              "active": true,
              "reason": null
            }
            """,

        ["EditPriceListItem"] = """
            {
              "code": "BLOUSE_PATTERN_STITCHING",
              "description": "Blouse stitching, pattern work",
              "kind": "Service",
              "baseRate": 472.5,
              "unit": "each",
              "taxCode": "STITCHING_5",
              "active": true,
              "reason": "Up by 5% with the new year's list."
            }
            """,

        ["RemovePriceListItem"] = """
            {
              "reason": "Entered twice; the other row is the one the catalogue names."
            }
            """,

        ["AddDiscountRule"] = """
            {
              "code": "FESTIVAL",
              "description": "Festival-season discount on stitching",
              "kind": "Percentage",
              "maximumWithoutApproval": 5,
              "maximum": 15,
              "active": true,
              "reason": null
            }
            """,

        ["EditDiscountRule"] = """
            {
              "code": "FESTIVAL",
              "description": "Festival-season discount on stitching",
              "kind": "Percentage",
              "maximumWithoutApproval": 5,
              "maximum": 20,
              "active": true,
              "reason": "The owner may now approve up to twenty percent."
            }
            """,

        ["RemoveDiscountRule"] = """
            {
              "reason": "Withdrawn after the season."
            }
            """,

        ["PublishPriceListVersion"] = """
            {
              "reason": "Approved by the accountant on 12 September; in force from 1 April 2027."
            }
            """,

        ["CreateTaxConfigurationDraft"] = """
            {
              "name": "Rates from 1 April 2027",
              "notes": "Cloned from version 2; the accountant's revised classification list.",
              "effectiveFrom": "2027-04-01",
              "cloneFromVersionId": "0199c2f0-0000-7000-8000-0000000000d2"
            }
            """,

        ["DescribeTaxConfigurationVersion"] = """
            {
              "name": "Rates from 1 April 2027",
              "notes": "The accountant's revised classification list.",
              "effectiveFrom": "2027-04-01",
              "reason": "The effective date moved to the start of the financial year."
            }
            """,

        ["AddTaxCode"] = """
            {
              "code": "STITCHING_5",
              "description": "Tailoring services",
              "classification": "998822",
              "kind": "Services",
              "active": true,
              "rates": [
                { "kind": "Cgst", "ratePercent": 2.5 },
                { "kind": "Sgst", "ratePercent": 2.5 },
                { "kind": "Igst", "ratePercent": 5 }
              ],
              "reason": null
            }
            """,

        ["EditTaxCode"] = """
            {
              "code": "STITCHING_5",
              "description": "Tailoring services, as the accountant classifies them",
              "classification": "998822",
              "kind": "Services",
              "active": true,
              "rates": [
                { "kind": "Cgst", "ratePercent": 2.5 },
                { "kind": "Sgst", "ratePercent": 2.5 },
                { "kind": "Igst", "ratePercent": 5 }
              ],
              "reason": "Description aligned with the accountant's wording."
            }
            """,

        ["RemoveTaxCode"] = """
            {
              "reason": "Entered twice; the other row is the one the price list names."
            }
            """,

        ["PublishTaxConfigurationVersion"] = """
            {
              "reason": "Approved by the accountant on 12 September; in force from 1 April 2027."
            }
            """,

        ["AddGstRegistration"] = """
            {
              "branchId": "0199c2f0-0000-7000-8000-0000000000a1",
              "gstin": "33AAACH7409R1Z8",
              "stateCode": "33",
              "legalName": "Example Tailors Private Limited",
              "tradeName": "Example Tailors",
              "effectiveFrom": "2026-04-01",
              "effectiveTo": null,
              "reason": null
            }
            """,

        ["AmendGstRegistration"] = """
            {
              "branchId": "0199c2f0-0000-7000-8000-0000000000a1",
              "gstin": "33AAACH7409R1Z8",
              "stateCode": "33",
              "legalName": "Example Tailors Private Limited",
              "tradeName": "Example Tailors",
              "effectiveFrom": "2026-04-01",
              "effectiveTo": "2027-03-31",
              "reason": "Re-registered under a new number from 1 April 2027."
            }
            """,

        ["CreateCatalogDraft"] = """
            {
              "name": "Add the Kids age bands",
              "notes": "Cloned from version 3 so the Aari links stay as published.",
              "cloneFromVersionId": "0199c2f0-0000-7000-8000-0000000000c3"
            }
            """,

        ["AddCatalogCategory"] = """
            {
              "code": "BLOUSE_AARI",
              "name": "Blouse — Aari work",
              "nameTamil": "\u0BB0\u0BB5\u0BBF\u0B95\u0BCD\u0B95\u0BC8 — \u0B86\u0BB0\u0BBF \u0BB5\u0BC7\u0BB2\u0BC8",
              "description": "Saree blouses carrying Aari hand embroidery.",
              "parentCategoryId": "0199c2f0-0000-7000-8000-0000000000b1",
              "displayOrder": 1,
              "activeFrom": null,
              "activeTo": null,
              "featureFlagKey": null,
              "branchIds": ["0199c2f0-0000-7000-8000-0000000000a1"],
              "reason": null
            }
            """,

        ["EditCatalogCategory"] = """
            {
              "code": "BLOUSE_AARI",
              "name": "Blouse — Aari work",
              "nameTamil": null,
              "description": "Saree blouses carrying Aari hand embroidery.",
              "parentCategoryId": "0199c2f0-0000-7000-8000-0000000000b1",
              "displayOrder": 2,
              "activeFrom": null,
              "activeTo": null,
              "featureFlagKey": "catalog.aari",
              "branchIds": ["0199c2f0-0000-7000-8000-0000000000a1"],
              "reason": "Aari moves below Pattern in the intake list, at the counter's request."
            }
            """,

        ["AddCatalogServiceType"] = """
            {
              "code": "STITCHING",
              "name": "Stitching",
              "nameTamil": null,
              "description": "A new garment cut and stitched from material the customer supplies.",
              "displayOrder": 0,
              "expectedDurationDays": 10,
              "intakeWarning": null,
              "measurementTemplateId": "0199c2f0-0000-7000-8000-0000000000d1",
              "workflowDefinitionId": "0199c2f0-0000-7000-8000-0000000000d2",
              "designOptionGroupIds": ["0199c2f0-0000-7000-8000-0000000000d3"],
              "priceListItemCode": "BLOUSE_AARI_STITCH",
              "qcChecklistTemplateId": "0199c2f0-0000-7000-8000-0000000000d4",
              "allowIncomplete": false,
              "activeFrom": null,
              "activeTo": null,
              "branchIds": ["0199c2f0-0000-7000-8000-0000000000a1"],
              "reason": null
            }
            """,

        ["EditCatalogServiceType"] = """
            {
              "code": "ALTERATION",
              "name": "Alteration",
              "nameTamil": null,
              "description": "Adjusting an existing finished garment.",
              "displayOrder": 1,
              "expectedDurationDays": 2,
              "intakeWarning": "An alteration crossing an embroidered area may damage the work.",
              "measurementTemplateId": "0199c2f0-0000-7000-8000-0000000000d1",
              "workflowDefinitionId": "0199c2f0-0000-7000-8000-0000000000d2",
              "designOptionGroupIds": [],
              "priceListItemCode": "BLOUSE_AARI_ALTER",
              "qcChecklistTemplateId": "0199c2f0-0000-7000-8000-0000000000d4",
              "allowIncomplete": false,
              "activeFrom": null,
              "activeTo": null,
              "branchIds": ["0199c2f0-0000-7000-8000-0000000000a1"],
              "reason": "The Tailor Master asked for the intake warning to be spelled out."
            }
            """,

        ["CorrectCatalogCategoryPresentation"] = """
            {
              "name": "Blouse — Pattern cut",
              "nameTamil": null,
              "description": "Plain and pattern-cut saree blouses.",
              "displayOrder": 0,
              "reason": "The counter reads 'Pattern cut' to customers; the label now matches."
            }
            """,

        ["CorrectCatalogServiceTypePresentation"] = """
            {
              "name": "Re-stitching",
              "nameTamil": null,
              "description": "Opening a garment and re-making it to a new fit.",
              "displayOrder": 2,
              "reason": "Spelling corrected after the owner review."
            }
            """,

        ["RemoveCatalogCategory"] = """
            {
              "reason": "Added by mistake; the code was meant for the sub-category."
            }
            """,

        ["RemoveCatalogServiceType"] = """
            {
              "reason": "The shop does not offer re-stitching on this category."
            }
            """,

        ["AddCatalogDesignGroup"] = """
            {
              "code": "sleeve_style",
              "name": "Sleeve length",
              "nameTamil": null,
              "selectionMode": "SingleChoice",
              "required": true,
              "displayOrder": 3,
              "activeFrom": null,
              "activeTo": null,
              "branchIds": ["0199c2f0-0000-7000-8000-0000000000a1"],
              "reason": null
            }
            """,

        ["EditCatalogDesignGroup"] = """
            {
              "code": "sleeve_style",
              "name": "Sleeve length",
              "nameTamil": null,
              "selectionMode": "SingleChoice",
              "required": true,
              "displayOrder": 3,
              "activeFrom": null,
              "activeTo": null,
              "branchIds": ["0199c2f0-0000-7000-8000-0000000000a1"],
              "reason": "Now offered at the second branch as well."
            }
            """,

        ["RemoveCatalogDesignGroup"] = """
            {
              "reason": "Added to the wrong category; it belongs on the gown."
            }
            """,

        ["AddCatalogDesignOption"] = """
            {
              "code": "THREE_QUARTER",
              "name": "Three-quarter sleeve",
              "nameTamil": null,
              "helpText": "Sleeve ends midway between elbow and wrist.",
              "illustrationKey": "design_blouse_sleeve_v1#sleeve_style.THREE_QUARTER",
              "illustrationAlt": "A sleeve ending halfway down the forearm, hemmed straight.",
              "priceListItemCode": null,
              "timeImpactDays": 0,
              "displayOrder": 4,
              "active": true,
              "reason": null
            }
            """,

        ["EditCatalogDesignOption"] = """
            {
              "code": "FULL",
              "name": "Full sleeve",
              "nameTamil": null,
              "helpText": "Sleeve ends at the wrist.",
              "illustrationKey": "design_blouse_sleeve_v1#sleeve_style.FULL",
              "illustrationAlt": "A sleeve reaching the wrist, hemmed straight.",
              "priceListItemCode": "PI_BLOUSE_FULL_SLEEVE",
              "timeImpactDays": 0,
              "displayOrder": 5,
              "active": true,
              "reason": "The full sleeve now carries its price-list item."
            }
            """,

        ["RemoveCatalogDesignOption"] = """
            {
              "reason": "Duplicated the cap sleeve under another code."
            }
            """,

        ["AddCatalogDesignRule"] = """
            {
              "type": "Requires",
              "antecedent": { "groupCode": "padding", "form": "In", "optionCodes": ["LIGHT", "MOULDED_CUP"] },
              "consequent": { "groupCode": "lining", "form": "In", "optionCodes": ["FULL", "KATORI_CUP"] },
              "note": null,
              "why": "Padding stitched against a single layer shows through and works loose.",
              "reason": null
            }
            """,

        ["EditCatalogDesignRule"] = """
            {
              "type": "Note",
              "antecedent": { "groupCode": "padding", "form": "Equals", "optionCodes": ["MOULDED_CUP"] },
              "consequent": null,
              "note": "Confirm the cup size against the customer's reference garment before cutting.",
              "why": "Cup sizing is not in the measurement set.",
              "reason": "Re-typed from an exclusion to a note after the owner review (OD-DES-04)."
            }
            """,

        ["RemoveCatalogDesignRule"] = """
            {
              "reason": "A shop preference, not a craft constraint; deleted at review (OD-DES-04)."
            }
            """,

        ["CorrectCatalogDesignGroupPresentation"] = """
            {
              "name": "Sleeve length",
              "nameTamil": "கை நீளம்",
              "displayOrder": 3,
              "reason": "Tamil label supplied after the native-speaker review."
            }
            """,

        ["CorrectCatalogDesignOptionPresentation"] = """
            {
              "name": "Three-quarter sleeve",
              "nameTamil": null,
              "helpText": "Sleeve ends midway between elbow and wrist.",
              "illustrationAlt": "A sleeve ending halfway down the forearm, hemmed straight.",
              "displayOrder": 4,
              "reason": "Clearer alternative text after the screen-reader walk."
            }
            """,

        ["CreateMeasurementTemplate"] = """
            {
              "code": "MT_BLOUSE_PATTERN",
              "name": "Blouse, Pattern",
              "description": "Linked from BLOUSE_PATTERN.STITCHING, .ALTERATION and .RESTITCHING."
            }
            """,

        ["StartMeasurementTemplateDraft"] = """
            {
              "name": "Version 2",
              "notes": "Widened the sleeve-round confirmation band after the October review.",
              "defaultDisplayUnit": "Inch",
              "cloneFromVersionId": "01a08000-0000-7000-8000-000000000001"
            }
            """,

        ["AddMeasurementTemplateField"] = """
            {
              "key": "front_neck_depth",
              "label": "Front neck depth",
              "labelTamil": null,
              "groupName": "Neckline",
              "displayOrder": 9,
              "canonicalUnit": "Millimetre",
              "inchFraction": 16,
              "centimetreDecimals": 1,
              "isRequired": true,
              "minimumMillimetres": 30,
              "maximumMillimetres": 450,
              "warnBelowMillimetres": 50,
              "warnAboveMillimetres": 300,
              "helpText": "Finished measurement. Shoulder-seam line at the neck, straight down the front to the neckline point.",
              "diagramKey": "blouse_front_v1",
              "diagramMediaId": null,
              "diagramAlt": "From the shoulder seam beside the neck, straight down the front to the neckline point.",
              "rule": null,
              "options": []
            }
            """,

        ["ChangeMeasurementTemplateField"] = """
            {
              "key": "sleeve_length",
              "label": "Sleeve length",
              "labelTamil": null,
              "groupName": "Sleeve",
              "displayOrder": 6,
              "canonicalUnit": "Millimetre",
              "inchFraction": 8,
              "centimetreDecimals": 1,
              "isRequired": true,
              "minimumMillimetres": 40,
              "maximumMillimetres": 800,
              "warnBelowMillimetres": 100,
              "warnAboveMillimetres": 650,
              "helpText": "Finished measurement. Shoulder point to the intended sleeve hem.",
              "diagramKey": "blouse_sleeve_v1",
              "diagramMediaId": null,
              "diagramAlt": "From the shoulder point down the outside of the arm to where the sleeve ends.",
              "rule": {
                "effect": "HiddenWhen",
                "anyOf": [
                  {
                    "scope": "DesignSelection",
                    "name": "sleeve_style",
                    "operator": "IsAnyOf",
                    "values": ["SLEEVELESS"]
                  }
                ]
              },
              "options": []
            }
            """,

        ["RemoveMeasurementTemplateField"] = """
            {
              "reason": "Superseded by cross_front and cross_back, which the Tailor Master measures instead."
            }
            """,

        ["SubmitMeasurementTemplateVersion"] = """
            {
              "reason": "Field set complete and checked against the paper register."
            }
            """,

        ["ReturnMeasurementTemplateVersion"] = """
            {
              "reason": "The armhole confirmation band is narrower than the sizes we actually see."
            }
            """,

        ["ApproveMeasurementTemplateVersion"] = """
            {
              "reason": "Reviewed field by field with the Tailor Master."
            }
            """,

        ["PublishMeasurementTemplateVersion"] = """
            {
              "reason": "Approved at the owner workshop on 9 September; supersedes version 1."
            }
            """,

        ["RetireMeasurementTemplateVersion"] = """
            {
              "reason": "This garment is no longer offered; no catalogue version references it."
            }
            """,

        ["PublishCatalogVersion"] = """
            {
              "reason": "Owner workshop approved the launch hierarchy on 9 September."
            }
            """,

        ["RetireCatalogVersion"] = """
            {
              "reason": "Superseded by the September hierarchy; no work is outstanding against it."
            }
            """,

        ["SuspendStaffUser"] = """
            {
              "reason": "Left the company on 5 September; access withdrawn at the manager's request."
            }
            """,

        ["ReinstateStaffUser"] = """
            {
              "reason": "Returned from unpaid leave; the manager confirmed the start date."
            }
            """,

        ["DeactivateStaffUser"] = """
            {
              "reason": "Resigned; last working day was 5 September."
            }
            """,

        ["ReactivateStaffUser"] = """
            {
              "reason": "Rejoined the shop; identity confirmed in person by the branch manager."
            }
            """,

        ["ResetStaffUserMfa"] = """
            {
              "reason": "Lost the phone holding the authenticator; identity confirmed in person."
            }
            """,

        ["RevokeStaffUserSessions"] = """
            {
              "reason": "Tablet left on a bus; signing every device out while it is recovered."
            }
            """,

        ["ReplaceStaffUserRoles"] = """
            {
              "roleKeys": ["tailor", "tailor_master"],
              "reason": "Promoted to master tailor; approved by the branch manager."
            }
            """,

        ["ReplaceStaffUserBranches"] = """
            {
              "branches": [
                { "branchId": "0199c000-0000-7000-8000-00000000000a", "isPrimary": true },
                { "branchId": "0199c000-0000-7000-8000-00000000000b", "isPrimary": false }
              ],
              "reason": "Covering the second branch two days a week from October."
            }
            """,

        ["InviteStaffUser"] = """
            {
              "userName": "priya.counter",
              "email": "priya.counter@synthetic.invalid",
              "displayName": "Priya R",
              "homeBranchId": "0199c000-0000-7000-8000-00000000000a",
              "reason": "Joining the counter team on 15 September; approved by the branch manager."
            }
            """,

        ["OpenBranch"] = """
            {
              "code": "MADURAI1",
              "name": "Madurai Main",
              "timeZoneId": "Asia/Kolkata",
              "addressLine1": "12 Example Street",
              "addressLine2": "Near the bus stand",
              "city": "Madurai",
              "state": "Tamil Nadu",
              "postalCode": "625001",
              "contactPhone": "+91 90000 00000",
              "contactEmail": "madurai@synthetic.invalid",
              "gstRegistrationReference": "GSTIN-EXAMPLE-0001",
              "reason": "Second location opening on 1 October."
            }
            """,

        ["ReconfigureBranch"] = """
            {
              "name": "Madurai Main",
              "timeZoneId": "Asia/Kolkata",
              "addressLine1": "14 Example Street",
              "addressLine2": "Near the bus stand",
              "city": "Madurai",
              "state": "Tamil Nadu",
              "postalCode": "625001",
              "contactPhone": "+91 90000 00001",
              "contactEmail": "madurai@synthetic.invalid",
              "gstRegistrationReference": "GSTIN-EXAMPLE-0001",
              "reason": "Moved two doors down; address and telephone updated."
            }
            """,

        ["CloseBranch"] = """
            {
              "reason": "Lease ended on 30 September; the counter has moved to Madurai Main."
            }
            """,

        ["ReopenBranch"] = """
            {
              "reason": "Reopening after the refit, from 1 December."
            }
            """,

        ["SetFeatureFlag"] = """
            {
              "enabled": true,
              "reason": "Enabling the new measurement sheet for the pilot branch trial."
            }
            """,

        ["SetModuleEnabled"] = """
            {
              "enabled": false,
              "reason": "Inventory is not in use until the stock count in November."
            }
            """,

        ["ExportAuditTrail"] = """
            {
              "entityType": "StaffUser",
              "entityId": "0192f3c1-9b1e-7a44-9a1b-1f9a0c2e77d1",
              "actorId": null,
              "action": "identity.user.",
              "from": "2026-09-01T00:00:00+05:30",
              "to": "2026-10-01T00:00:00+05:30",
              "cursor": null,
              "limit": 500,
              "reason": "Quarterly access review requested by the owner."
            }
            """,

        ["SignIn"] = """
            {
              "identifier": "counter.demo",
              "password": "example-passphrase-not-a-real-credential",
              "captchaResponse": null
            }
            """,

        ["AnswerMultiFactorChallenge"] = """
            {
              "factor": "totp",
              "code": "000000",
              "rememberDevice": false
            }
            """,

        ["ConfirmMultiFactorEnrolment"] = """
            {
              "code": "000000"
            }
            """,

        ["CompletePasskeyRegistration"] = """
            {
              "ceremonyId": "0192f3c1-9b1e-7a44-9a1b-1f9a0c2e77d1",
              "credential": {
                "id": "ZXhhbXBsZS1jcmVkZW50aWFsLWlkZW50aWZpZXI",
                "rawId": "ZXhhbXBsZS1jcmVkZW50aWFsLWlkZW50aWZpZXI",
                "type": "public-key",
                "response": {
                  "attestationObject": "ZXhhbXBsZS1hdHRlc3RhdGlvbi1vYmplY3Q",
                  "clientDataJSON": "ZXhhbXBsZS1jbGllbnQtZGF0YQ"
                }
              },
              "label": "Counter tablet"
            }
            """,

        ["CompletePasskeyAssertion"] = """
            {
              "ceremonyId": "0192f3c1-9b1e-7a44-9a1b-1f9a0c2e77d1",
              "credential": {
                "id": "ZXhhbXBsZS1jcmVkZW50aWFsLWlkZW50aWZpZXI",
                "rawId": "ZXhhbXBsZS1jcmVkZW50aWFsLWlkZW50aWZpZXI",
                "type": "public-key",
                "response": {
                  "authenticatorData": "ZXhhbXBsZS1hdXRoZW50aWNhdG9yLWRhdGE",
                  "clientDataJSON": "ZXhhbXBsZS1jbGllbnQtZGF0YQ",
                  "signature": "ZXhhbXBsZS1zaWduYXR1cmU",
                  "userHandle": null
                }
              }
            }
            """,

        ["DefineRole"] = """
            {
              "key": "senior_cashier",
              "name": "Senior Cashier",
              "description": "A cashier who may also approve a refund and close the day's session.",
              "reach": "Branch",
              "reason": "The Erode counter needs somebody who can close the day when the manager is away."
            }
            """,

        ["DescribeRole"] = """
            {
              "name": "Senior Cashier",
              "description": "A cashier who may also approve a refund and close the day's session.",
              "reason": "The old description said 'counter lead', which nobody in the shop calls it."
            }
            """,

        ["ReplaceRolePermissions"] = """
            {
              "permissionKeys": [
                "billing.invoice.read",
                "billing.payment.record",
                "billing.session.close"
              ],
              "reason": "Approved at the September operations review; the counter closes its own session from Monday."
            }
            """,

        ["DeleteRole"] = """
            {
              "reason": "The trial of a separate measurement role ended; nobody was ever assigned to it."
            }
            """,

        ["ReplayOutboxMessage"] = """
            {
              "reason": "The notification provider outage was resolved at 09:40; the customer was never told their order was ready."
            }
            """,

        ["RegisterCustomer"] = """
            {
              "displayName": "Lakshmi Ramanathan",
              "nativeName": "\u0BB2\u0B9F\u0BCD\u0B9A\u0BC1\u0BAE\u0BBF",
              "phone": "+91 90000 00021",
              "alternatePhone": null,
              "email": "lakshmi.demo@example.invalid",
              "addressLine": "12 Second Street, Demo Nagar",
              "locality": "Peelamedu",
              "postcode": "641004",
              "language": "ta-IN",
              "duplicatesReviewed": false
            }
            """,

        ["CorrectCustomer"] = """
            {
              "displayName": "Lakshmi Sundaram",
              "nativeName": "\u0BB2\u0B9F\u0BCD\u0B9A\u0BC1\u0BAE\u0BBF",
              "phone": "+91 90000 00021",
              "alternatePhone": "+91 90000 00022",
              "email": "lakshmi.demo@example.invalid",
              "addressLine": "12 Second Street, Demo Nagar",
              "locality": "Peelamedu",
              "postcode": "641004",
              "language": "ta-IN",
              "reason": "Married in August and asked for the new surname on her receipts."
            }
            """,

        ["DeactivateCustomer"] = """
            {
              "reason": "Moved out of the city and asked us not to contact her about new offers."
            }
            """,

        ["ReactivateCustomer"] = """
            {
              "reason": "Moved back and came in for a blouse; she asked us to use the old record."
            }
            """,

        ["ExportCustomer"] = """
            {
              "reason": "Subject-access request received at the Gandhipuram counter on 6 September and verified against the number on file."
            }
            """,

        ["MergeCustomers"] = """
            {
              "mergedCustomerId": "019bcfa3-6c81-7e94-b025-3a4b5c6d7e8f",
              "mergedCustomerVersion": "8241",
              "reason": "Same phone number, same address and she confirmed at the counter that the second record was created when the Gandhipuram branch could not see the first."
            }
            """,

        ["RecordCustomerConsent"] = """
            {
              "purposeKey": "photo_capture",
              "decision": "Granted",
              "source": "counter, verbal"
            }
            """,

        ["ReplaceCustomerCommunicationPreferences"] = """
            {
              "allowedChannels": ["Sms", "WhatsApp"],
              "language": "ta-IN",
              "quietHoursStart": "21:30:00",
              "quietHoursEnd": "08:00:00"
            }
            """,

        ["RequestPasswordRecovery"] = """
            {
              "email": "counter.demo@example.invalid"
            }
            """,

        ["ConfirmPasswordRecovery"] = """
            {
              "token": "bm90LWEtcmVhbC1yZWNvdmVyeS10b2tlbg",
              "newPassword": "example-passphrase-not-a-real-credential"
            }
            """,

        // Measuring a garment (issue #121). The values are sent as they were typed, with the unit beside
        // each: the server converts, so a client that rounds differently cannot store a number the server
        // would never have produced. Every measurement below is synthetic.
        ["StartMeasurementDraft"] = """
            {
              "customerId": "0199c2f0-0000-7000-8000-0000000000b1",
              "measurementTemplateId": "0199c2f0-0000-7000-8000-0000000000f1",
              "reuseFromVersionId": null
            }
            """,

        ["SaveMeasurementSection"] = """
            {
              "groupName": "Bodice",
              "values": [
                {
                  "key": "chest_bust",
                  "entered": 36.125,
                  "unit": "Inch",
                  "choice": null,
                  "acknowledged": false
                },
                {
                  "key": "sleeve_style",
                  "entered": null,
                  "unit": null,
                  "choice": "PUFF",
                  "acknowledged": false
                }
              ]
            }
            """,

        ["ConfirmMeasurements"] = """
            {
              "reason": "The shoulder was re-measured after the first fitting.",
              "correctsVersionId": "0199c2f0-0000-7000-8000-0000000000e1"
            }
            """,
    };

    /// <summary>The example for an operation, or <see langword="null"/> when none is registered.</summary>
    /// <param name="operationId">The operation identifier, as declared by <c>WithName</c>.</param>
    /// <returns>A parsed example, or <see langword="null"/>.</returns>
    public static JsonNode? For(string operationId)
        => Examples.TryGetValue(operationId, out var json) ? JsonNode.Parse(json) : null;

    /// <summary>The operation identifiers an example is registered for.</summary>
    public static IReadOnlyCollection<string> RegisteredOperations => Examples.Keys;
}
