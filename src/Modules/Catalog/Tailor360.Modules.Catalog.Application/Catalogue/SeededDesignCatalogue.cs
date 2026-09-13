using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Section 9 of <c>docs/prd/design-options.md</c>, transcribed: the design option groups, their options
/// and the <c>DR-01</c>–<c>DR-44</c> rules, for the six orderable categories of
/// <see cref="SeededCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a transcription, not a design (issue #139). Codes, labels, illustration sheets, price-list
/// item codes and day impacts are copied from the document; a value with no source there is not here.
/// Two fields the document requires of every option — <see cref="SeededOption"/>'s help text and
/// alternative text — have no literal per-option sentence in section 9 (only the label does), so they
/// are derived mechanically from the group and option labels by <see cref="HelpTextFor"/> and
/// <see cref="IllustrationAltFor"/> rather than invented per option; see the pull request description
/// for the correction this seed made to the document where the seed found it inconsistent (DR-03,
/// DR-04, DR-14 and their <c>BLOUSE_AARI</c> copies).
/// </para>
/// <para>
/// The catalogue holds a price-list item <em>code</em> and a day impact, never an amount — Section 5:
/// "the catalogue holds the code, never the money". The proportional Aari density and stone options and
/// the <c>aari_placement</c> day cap of Section 9.2 are therefore seeded as plain codes and day figures
/// like every other option; the proportional-amount and capping <em>behaviour</em> the document proposes
/// is not represented by the current model and is flagged <b>OD-DES-02</b> and <b>OD-DES-03</b> rather
/// than approximated here.
/// </para>
/// </remarks>
public static class SeededDesignCatalogue
{
    /// <summary>The sixteen bundled line-drawing sheets of Section 6, registered by key only (OD-DES-07).</summary>
    public static class Sheets
    {
        public const string BlouseCut = "design_blouse_cut_v1";
        public const string BlouseFrontNeck = "design_blouse_front_neck_v1";
        public const string BlouseBackNeck = "design_blouse_back_neck_v1";
        public const string BlouseSleeve = "design_blouse_sleeve_v1";
        public const string BlouseClosure = "design_blouse_closure_v1";
        public const string BlouseFinish = "design_blouse_finish_v1";
        public const string AariMotif = "design_aari_motif_v1";
        public const string AariPlacement = "design_aari_placement_v1";
        public const string SalwarKameez = "design_salwar_kameez_v1";
        public const string SalwarNeck = "design_salwar_neck_v1";
        public const string SalwarBottom = "design_salwar_bottom_v1";
        public const string LehengaSkirt = "design_lehenga_skirt_v1";
        public const string LehengaDupatta = "design_lehenga_dupatta_v1";
        public const string GownSilhouette = "design_gown_silhouette_v1";
        public const string GownNeckline = "design_gown_neckline_v1";
        public const string KidsStyle = "design_kids_style_v1";
    }

    /// <summary>One seeded option of a group: code, label and its impacts, in document order.</summary>
    /// <param name="Code">The <c>UPPER_SNAKE_CASE</c> code.</param>
    /// <param name="Name">The label, as section 9 gives it.</param>
    /// <param name="PriceListItemCode">The price-list item code, or null for "—".</param>
    /// <param name="DayImpact">The signed working-day impact, or zero for "—".</param>
    public sealed record SeededOption(string Code, string Name, string? PriceListItemCode = null, int DayImpact = 0);

    /// <summary>One seeded design option group of one category, in document order.</summary>
    /// <param name="CategoryCode">The category it belongs to.</param>
    /// <param name="Code">The <c>lower_snake_case</c> group code.</param>
    /// <param name="Name">The label, as the category's own groups table gives it.</param>
    /// <param name="SelectionMode">Single or multiple.</param>
    /// <param name="Required">Whether a garment may be confirmed with nothing chosen here.</param>
    /// <param name="IllustrationSheet">The bundled sheet the group's options are anchored on.</param>
    /// <param name="Options">The options, in document order.</param>
    public sealed record SeededGroup(
        string CategoryCode,
        string Code,
        string Name,
        DesignSelectionMode SelectionMode,
        bool Required,
        string IllustrationSheet,
        IReadOnlyList<SeededOption> Options);

    /// <summary>One seeded rule, <c>DR-01</c> to <c>DR-44</c>.</summary>
    /// <param name="Number">The number in <c>DR-nn</c>.</param>
    /// <param name="CategoryCode">The category whose groups it reads.</param>
    /// <param name="Type">Which of the four types it is.</param>
    /// <param name="Antecedent">What has to hold for the rule to fire.</param>
    /// <param name="Consequent">The option set a requires/excludes rule names, or null.</param>
    /// <param name="Note">The standing instruction a note attaches, or the printed text of a re-typed rule.</param>
    /// <param name="Why">The reason the rule exists, shortened from the document's own "Why" column.</param>
    public sealed record SeededRule(
        int Number,
        string CategoryCode,
        DesignRuleType Type,
        DesignRuleOperand Antecedent,
        DesignRuleOperand? Consequent,
        string? Note,
        string? Why);

    private const string BlousePattern = "BLOUSE_PATTERN";
    private const string BlouseAari = "BLOUSE_AARI";
    private const string Salwar = "SALWAR";
    private const string Lehenga = "LEHENGA";
    private const string Gown = "GOWN";
    private const string Kids = "KIDS";

    // The six-option sleeve length and four-option sleeve shape sets that Section 9.1 prices and that
    // every other category re-seeds "with no price or time impact" (9.3, 9.4, 9.5) unless stated
    // otherwise. BLOUSE_AARI is the one category the document does not say that about — 9.2 says its
    // nine core groups are seeded again "with the same codes and the same meanings", so its copy below
    // keeps BLOUSE_PATTERN's own impacts. That reading is a candidate for review; see the pull request
    // description.
    private static IReadOnlyList<SeededOption> SleeveStyleSix(bool withImpact) =>
    [
        new("SLEEVELESS", "Sleeveless"),
        new("CAP", "Cap sleeve"),
        new("SHORT", "Short sleeve"),
        new("ELBOW", "Elbow sleeve"),
        new("THREE_QUARTER", "Three-quarter sleeve"),
        withImpact
            ? new("FULL", "Full sleeve", "PI_BLOUSE_FULL_SLEEVE")
            : new("FULL", "Full sleeve"),
    ];

    private static IReadOnlyList<SeededOption> SleeveShapeFour(bool withImpact) =>
    [
        new("PLAIN", "Plain sleeve"),
        withImpact ? new("PUFF", "Puff sleeve", "PI_BLOUSE_PUFF") : new("PUFF", "Puff sleeve"),
        withImpact ? new("BELL", "Bell sleeve", "PI_BLOUSE_BELL") : new("BELL", "Bell sleeve"),
        withImpact ? new("FRILL", "Frill sleeve", "PI_BLOUSE_FRILL") : new("FRILL", "Frill sleeve"),
    ];

    /// <summary>The nine <c>BLOUSE_PATTERN</c> groups of Section 9.1, reused verbatim by <c>BLOUSE_AARI</c>.</summary>
    private static IReadOnlyList<SeededGroup> BlouseCoreGroups(string categoryCode, bool withImpact) =>
    [
        new(categoryCode, "blouse_cut", "Blouse cut", DesignSelectionMode.SingleChoice, true, Sheets.BlouseCut,
        [
            new("PLAIN_DART", "Plain, darted"),
            new("PRINCESS_CUT", "Princess cut"),
            new("KATORI", "Katori (cup)"),
            new("PAITHANI", "Paithani"),
        ]),
        new(categoryCode, "front_neck", "Front neck shape", DesignSelectionMode.SingleChoice, true,
            Sheets.BlouseFrontNeck,
        [
            new("ROUND", "Round neck"),
            new("DEEP_ROUND", "Deep round neck"),
            new("V_NECK", "V neck"),
            new("SWEETHEART", "Sweetheart neck"),
            new("BOAT", "Boat neck"),
            withImpact
                ? new("HIGH_NECK", "High neck", "PI_BLOUSE_HIGH_NECK")
                : new("HIGH_NECK", "High neck"),
            new("SQUARE", "Square neck"),
        ]),
        new(categoryCode, "back_neck", "Back neck shape", DesignSelectionMode.SingleChoice, true,
            Sheets.BlouseBackNeck,
        [
            new("ROUND", "Round back"),
            new("ROUND_DEEP", "Deep round back"),
            new("V_DEEP", "Deep V back"),
            new("U_DEEP", "Deep U back"),
            withImpact
                ? new("KEYHOLE", "Keyhole back", "PI_BLOUSE_KEYHOLE")
                : new("KEYHOLE", "Keyhole back"),
            new("HIGH_NECK", "High back"),
        ]),
        new(categoryCode, "sleeve_style", "Sleeve length", DesignSelectionMode.SingleChoice, true,
            Sheets.BlouseSleeve, SleeveStyleSix(withImpact)),
        new(categoryCode, "sleeve_shape", "Sleeve shape", DesignSelectionMode.SingleChoice, false,
            Sheets.BlouseSleeve, SleeveShapeFour(withImpact)),
        new(categoryCode, "closure", "Closure", DesignSelectionMode.SingleChoice, true, Sheets.BlouseClosure,
        [
            new("HOOK", "Hooks, back"),
            new("HOOK_FRONT", "Hooks, front"),
            withImpact
                ? new("ZIP_BACK", "Zip, back", "PI_BLOUSE_ZIP")
                : new("ZIP_BACK", "Zip, back"),
            withImpact
                ? new("ZIP_SIDE", "Zip, side", "PI_BLOUSE_ZIP")
                : new("ZIP_SIDE", "Zip, side"),
            withImpact
                ? new("TIE_BACK", "Tie back (dori)", "PI_BLOUSE_TIE")
                : new("TIE_BACK", "Tie back (dori)"),
        ]),
        new(categoryCode, "lining", "Lining and cup", DesignSelectionMode.SingleChoice, true, Sheets.BlouseFinish,
        [
            new("NONE", "No lining"),
            withImpact
                ? new("FULL", "Full lining", "PI_BLOUSE_LINING_FULL")
                : new("FULL", "Full lining"),
            withImpact
                ? new("KATORI_CUP", "Katori cup lining", "PI_BLOUSE_LINING_KATORI")
                : new("KATORI_CUP", "Katori cup lining"),
        ]),
        new(categoryCode, "padding", "Padding", DesignSelectionMode.SingleChoice, false, Sheets.BlouseFinish,
        [
            new("NONE", "No padding"),
            withImpact
                ? new("LIGHT", "Light padding", "PI_BLOUSE_PADDING")
                : new("LIGHT", "Light padding"),
            withImpact
                ? new("MOULDED_CUP", "Moulded cup", "PI_BLOUSE_PADDING_CUP")
                : new("MOULDED_CUP", "Moulded cup"),
        ]),
        new(categoryCode, "finish", "Edge finish", DesignSelectionMode.MultipleChoice, false, Sheets.BlouseFinish,
        [
            withImpact ? new("PIPING", "Piping", "PI_BLOUSE_PIPING") : new("PIPING", "Piping"),
            withImpact
                ? new("CONTRAST_BINDING", "Contrast binding", "PI_BLOUSE_BINDING")
                : new("CONTRAST_BINDING", "Contrast binding"),
            withImpact
                ? new("LACE_EDGE", "Lace edging", "PI_BLOUSE_LACE")
                : new("LACE_EDGE", "Lace edging"),
        ]),
    ];

    /// <summary>The rules DR-01, DR-02, DR-03, DR-04, DR-05, DR-06 and DR-31, numbered for one category.</summary>
    private static IReadOnlyList<SeededRule> BlouseCoreRules(
        string categoryCode, int padLining, int katoriLining, int closedNeckNote, int sleevelessLace,
        int zipNote, int mouldedCupNote, int sleeveShapeExcludesSleeveless) =>
    [
        new(padLining, categoryCode, DesignRuleType.Requires,
            new DesignRuleOperand("padding", DesignOperandForm.In, ["LIGHT", "MOULDED_CUP"]),
            new DesignRuleOperand("lining", DesignOperandForm.NotEquals, ["NONE"]),
            null,
            "Padding stitched against a single layer shows through and works loose."),
        new(katoriLining, categoryCode, DesignRuleType.Requires,
            new DesignRuleOperand("blouse_cut", DesignOperandForm.Equals, ["KATORI"]),
            new DesignRuleOperand("lining", DesignOperandForm.Equals, ["KATORI_CUP"]),
            null,
            "A katori blouse is defined by its cup seam; the cups cannot be cut unlined."),
        // DR-03 (and its BLOUSE_AARI copy DR-34): the document's own statement joins two conditions with
        // "with" ("front_neck = HIGH_NECK with back_neck = HIGH_NECK"), a compound the rule grammar of
        // Section 4 does not define — every operand form there is over one group — and the merged rule
        // model (#137) carries exactly one antecedent operand per rule. The correction seeded here, and
        // proposed for the document, anchors the note on the front neck alone and folds the back-neck
        // qualification into the printed text, which a non-blocking note can safely do.
        new(closedNeckNote, categoryCode, DesignRuleType.Note,
            new DesignRuleOperand("front_neck", DesignOperandForm.Equals, ["HIGH_NECK"]),
            null,
            "Closed neck front and back: confirm the opening — back hooks or a side zip. Applies when "
            + "the back neck is also high; check the back-neck selection before printing the summary.",
            "The neckline height is not what opens the garment; the closure is (OD-DES-04)."),
        // DR-04 (and its copy DR-35): the document's statement reads "finish = LACE_EDGE", but `finish`
        // is seeded as a multiple-selection group (its own groups table says so), and Section 4 reserves
        // `=` for a single-selection group's value — `includes` is the form a multiple-selection group
        // takes. Corrected here and in the document to `finish includes LACE_EDGE`.
        new(sleevelessLace, categoryCode, DesignRuleType.Excludes,
            new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]),
            new DesignRuleOperand("finish", DesignOperandForm.Includes, ["LACE_EDGE"]),
            null,
            "Seeded as a shop preference, not a physical law (OD-DES-04, flagged for deletion at review)."),
        new(zipNote, categoryCode, DesignRuleType.Note,
            new DesignRuleOperand("closure", DesignOperandForm.In, ["ZIP_BACK", "ZIP_SIDE"]),
            null,
            "Match the zip tape to the shell fabric; check the zip runs freely after lining.",
            "Craft instruction, printed on the job card."),
        new(mouldedCupNote, categoryCode, DesignRuleType.Note,
            new DesignRuleOperand("padding", DesignOperandForm.Equals, ["MOULDED_CUP"]),
            null,
            "Confirm the cup size against the customer's reference garment before cutting.",
            "Cup sizing is not in the measurement set."),
        new(sleeveShapeExcludesSleeveless, categoryCode, DesignRuleType.Excludes,
            new DesignRuleOperand("sleeve_shape", DesignOperandForm.NotEquals, ["PLAIN"]),
            new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]),
            null,
            "There is no sleeve to shape. sleeve_shape is not required, so a sleeveless blouse leaves it unset."),
    ];

    /// <summary>The 56 seeded design option groups across the six orderable categories.</summary>
    public static IReadOnlyList<SeededGroup> Groups { get; } = BuildGroups();

    /// <summary>The 44 seeded rules, <c>DR-01</c> to <c>DR-44</c>.</summary>
    public static IReadOnlyList<SeededRule> Rules { get; } = BuildRules();

    /// <summary>
    /// The service types that offer the design groups of a category — <c>STITCHING</c> and
    /// <c>RESTITCHING</c> carry the full set, <c>ALTERATION</c> carries none (Section 9, OD-DES-01).
    /// </summary>
    public static IReadOnlyList<string> ServiceCodesOffered { get; } = ["STITCHING", "RESTITCHING"];

    /// <summary>
    /// A mechanical one-sentence help text, derived from the sourced group and option labels. Section 9
    /// gives no literal help-text sentence per option (only the label), so this is authored rather than
    /// transcribed; see the pull request description.
    /// </summary>
    /// <param name="groupName">The owning group's label.</param>
    /// <param name="optionName">The option's own label.</param>
    /// <returns>A short sentence stating the choice, never inventing a construction detail the document does not give.</returns>
    public static string HelpTextFor(string groupName, string optionName)
        => $"{optionName} — the {LowerFirst(groupName)} chosen for this garment.";

    /// <summary>
    /// A mechanical alternative-text sentence, derived the same way as <see cref="HelpTextFor"/>. Every
    /// option needs one (Section 6) because the bundled drawings arrive with OD-DES-07 and, until then,
    /// the picker falls back to label plus alt text.
    /// </summary>
    /// <param name="groupName">The owning group's label.</param>
    /// <param name="optionName">The option's own label.</param>
    /// <returns>The shape in words, as far as the label alone states it.</returns>
    public static string IllustrationAltFor(string groupName, string optionName)
        => $"Illustrates {optionName.ToLowerInvariant()} for {LowerFirst(groupName)}.";

    private static string LowerFirst(string value)
        => value.Length == 0 ? value : string.Concat(char.ToLowerInvariant(value[0]), value[1..]);

    private static List<SeededGroup> BuildGroups()
    {
        var groups = new List<SeededGroup>();

        // 9.1 BLOUSE_PATTERN — nine groups, priced.
        groups.AddRange(BlouseCoreGroups(BlousePattern, withImpact: true));

        // 9.2 BLOUSE_AARI — the same nine groups again ("the same codes and the same meanings"), plus
        // the four embroidery groups.
        groups.AddRange(BlouseCoreGroups(BlouseAari, withImpact: true));
        groups.Add(new SeededGroup(BlouseAari, "aari_motif", "Aari motif", DesignSelectionMode.MultipleChoice,
            true, Sheets.AariMotif,
        [
            new("PEACOCK_MEDIUM", "Peacock, medium"),
            new("PEACOCK_LARGE", "Peacock, large", "PI_AARI_MOTIF_LARGE", 2),
            new("MANGO", "Mango (maanga)"),
            new("LOTUS", "Lotus"),
            new("FLORAL_VINE", "Floral vine"),
            new("TEMPLE_BORDER", "Temple border"),
            new("GEOMETRIC", "Geometric"),
            new("CUSTOM_REFERENCE", "To customer's reference image", "PI_AARI_MOTIF_CUSTOM", 2),
        ]));
        groups.Add(new SeededGroup(BlouseAari, "aari_density", "Work density", DesignSelectionMode.SingleChoice,
            true, Sheets.AariMotif,
        [
            new("LIGHT", "Light", "PI_AARI_DENSITY_LIGHT", -2),
            new("MEDIUM", "Medium"),
            new("HEAVY", "Heavy (bridal)", "PI_AARI_DENSITY_HEAVY", 3),
        ]));
        groups.Add(new SeededGroup(BlouseAari, "aari_stone", "Stone and bead type", DesignSelectionMode.MultipleChoice,
            true, Sheets.AariMotif,
        [
            new("AD_STONE", "AD stones"),
            new("KUNDAN", "Kundan", "PI_AARI_KUNDAN", 1),
            new("BEADS", "Beads"),
            new("ZARI_THREAD", "Zari thread"),
            new("MIRROR", "Mirror work", "PI_AARI_MIRROR", 1),
        ]));
        // The aari_placement cap (OD-DES-03): the document proposes the group's day impacts are capped
        // to the maximum of the selected placements rather than summed, because the placements are
        // worked in one mounting. The current model has no field for a group-level cap, so the plain
        // per-option day impacts below are seeded as the document gives them and the capping behaviour
        // itself is left to OD-DES-03, unimplemented, rather than approximated.
        groups.Add(new SeededGroup(BlouseAari, "aari_placement", "Work placement", DesignSelectionMode.MultipleChoice,
            true, Sheets.AariPlacement,
        [
            new("FRONT", "Front panel", "PI_AARI_FRONT", 3),
            new("BACK", "Back panel", "PI_AARI_BACK", 3),
            new("NECK", "Neckline band", "PI_AARI_NECK", 1),
            new("SLEEVE", "Sleeve bands, pair", "PI_AARI_SLEEVE", 1),
            new("BORDER", "Hem border", "PI_AARI_BORDER", 1),
        ]));

        // 9.3 SALWAR — eight groups.
        groups.Add(new SeededGroup(Salwar, "kameez_style", "Kameez cut", DesignSelectionMode.SingleChoice, true,
            Sheets.SalwarKameez,
        [
            new("STRAIGHT", "Straight cut"),
            new("A_LINE", "A-line"),
            new("ANARKALI", "Anarkali", "PI_SALWAR_ANARKALI", 1),
            new("UMBRELLA", "Umbrella cut", "PI_SALWAR_UMBRELLA", 1),
            new("SHORT_TOP", "Short top"),
        ]));
        groups.Add(new SeededGroup(Salwar, "kameez_neck", "Kameez neck", DesignSelectionMode.SingleChoice, true,
            Sheets.SalwarNeck,
        [
            new("ROUND", "Round neck"),
            new("V_NECK", "V neck"),
            new("BOAT", "Boat neck"),
            new("COLLAR", "Collar", "PI_SALWAR_COLLAR"),
            new("KEYHOLE", "Keyhole"),
        ]));
        groups.Add(new SeededGroup(Salwar, "sleeve_style", "Sleeve length", DesignSelectionMode.SingleChoice, true,
            Sheets.BlouseSleeve, SleeveStyleSix(withImpact: false)));
        groups.Add(new SeededGroup(Salwar, "sleeve_shape", "Sleeve shape", DesignSelectionMode.SingleChoice, false,
            Sheets.BlouseSleeve, SleeveShapeFour(withImpact: false)));
        groups.Add(new SeededGroup(Salwar, "kameez_slit", "Kameez slit", DesignSelectionMode.SingleChoice, true,
            Sheets.SalwarNeck,
        [
            new("NONE", "No slit"),
            new("SIDE_SHORT", "Short side slit"),
            new("SIDE_HIGH", "High side slit", "PI_SALWAR_SLIT_HIGH"),
            new("FRONT_SLIT", "Front slit", "PI_SALWAR_SLIT_FRONT"),
        ]));
        groups.Add(new SeededGroup(Salwar, "bottom_style", "Bottom style", DesignSelectionMode.SingleChoice, true,
            Sheets.SalwarBottom,
        [
            new("SALWAR", "Salwar"),
            new("CHURIDAR", "Churidar"),
            new("PANT", "Pant"),
            new("PALAZZO", "Palazzo", "PI_SALWAR_PALAZZO"),
            new("PATIALA", "Patiala", "PI_SALWAR_PATIALA", 1),
        ]));
        groups.Add(new SeededGroup(Salwar, "lining", "Lining", DesignSelectionMode.SingleChoice, false,
            Sheets.SalwarNeck,
        [
            new("NONE", "No lining"),
            new("KAMEEZ_ONLY", "Kameez lined", "PI_SALWAR_LINING"),
            new("FULL", "Both pieces lined", "PI_SALWAR_LINING_FULL", 1),
        ]));
        groups.Add(new SeededGroup(Salwar, "dupatta_finish", "Dupatta finish", DesignSelectionMode.SingleChoice,
            false, Sheets.LehengaDupatta,
        [
            new("NONE", "No dupatta"),
            new("HEM_ONLY", "Hemmed"),
            new("LACE_BORDER", "Lace border", "PI_SALWAR_DUPATTA_LACE", 1),
            new("TASSELS", "Tassels", "PI_SALWAR_DUPATTA_TASSEL"),
        ]));

        // 9.4 LEHENGA — ten groups. The choli reuses the blouse groups' option lists, seeded here with
        // no price or time impact because the lehenga base rate is quoted for a lined, fastened choli.
        var choliOptions = BlouseCoreGroups(Lehenga, withImpact: false);
        groups.Add(new SeededGroup(Lehenga, "front_neck", "Choli front neck", DesignSelectionMode.SingleChoice,
            true, Sheets.BlouseFrontNeck, choliOptions[1].Options));
        groups.Add(new SeededGroup(Lehenga, "back_neck", "Choli back", DesignSelectionMode.SingleChoice, true,
            Sheets.BlouseBackNeck, choliOptions[2].Options));
        groups.Add(new SeededGroup(Lehenga, "sleeve_style", "Choli sleeve length", DesignSelectionMode.SingleChoice,
            true, Sheets.BlouseSleeve, SleeveStyleSix(withImpact: false)));
        groups.Add(new SeededGroup(Lehenga, "sleeve_shape", "Choli sleeve shape", DesignSelectionMode.SingleChoice,
            false, Sheets.BlouseSleeve, SleeveShapeFour(withImpact: false)));
        groups.Add(new SeededGroup(Lehenga, "closure", "Choli closure", DesignSelectionMode.SingleChoice, true,
            Sheets.BlouseClosure, choliOptions[5].Options));
        groups.Add(new SeededGroup(Lehenga, "lining", "Choli lining", DesignSelectionMode.SingleChoice, true,
            Sheets.BlouseFinish, choliOptions[6].Options));
        groups.Add(new SeededGroup(Lehenga, "padding", "Choli padding", DesignSelectionMode.SingleChoice, false,
            Sheets.BlouseFinish, choliOptions[7].Options));
        groups.Add(new SeededGroup(Lehenga, "skirt_style", "Skirt style", DesignSelectionMode.SingleChoice, true,
            Sheets.LehengaSkirt,
        [
            new("KALI", "Kali (panelled)"),
            new("CIRCULAR", "Circular", "PI_LEHENGA_CIRCULAR", 2),
            new("A_LINE", "A-line"),
            new("MERMAID", "Mermaid", "PI_LEHENGA_MERMAID", 2),
            new("LAYERED", "Layered with can-can", "PI_LEHENGA_LAYERED", 3),
        ]));
        groups.Add(new SeededGroup(Lehenga, "waist_finish", "Skirt waist finish", DesignSelectionMode.MultipleChoice,
            true, Sheets.LehengaSkirt,
        [
            new("HOOK_BAND", "Hook and band"),
            new("ZIP", "Concealed zip", "PI_LEHENGA_WAIST_ZIP"),
            new("DRAWSTRING", "Drawstring (nada)"),
            new("ELASTIC_BACK", "Elastic at back only"),
        ]));
        groups.Add(new SeededGroup(Lehenga, "dupatta", "Dupatta", DesignSelectionMode.SingleChoice, true,
            Sheets.LehengaDupatta,
        [
            new("NONE", "No dupatta"),
            new("PLAIN", "Plain dupatta"),
            new("BORDER_WORK", "Dupatta with border work"),
            new("DOUBLE", "Two dupattas"),
        ]));

        // 9.5 GOWN — ten groups.
        groups.Add(new SeededGroup(Gown, "silhouette", "Silhouette", DesignSelectionMode.SingleChoice, true,
            Sheets.GownSilhouette,
        [
            new("A_LINE", "A-line"),
            new("FLARED", "Flared"),
            new("FITTED", "Fitted", "PI_GOWN_FITTED", 1),
            new("MERMAID", "Mermaid", "PI_GOWN_MERMAID", 2),
            new("EMPIRE", "Empire line"),
        ]));
        groups.Add(new SeededGroup(Gown, "neckline", "Neckline", DesignSelectionMode.SingleChoice, true,
            Sheets.GownNeckline,
        [
            new("ROUND", "Round"),
            new("BOAT", "Boat"),
            new("V_NECK", "V neck"),
            new("SWEETHEART", "Sweetheart"),
            new("HALTER", "Halter", "PI_GOWN_HALTER"),
            new("OFF_SHOULDER", "Off shoulder", "PI_GOWN_OFF_SHOULDER", 1),
        ]));
        groups.Add(new SeededGroup(Gown, "sleeve_style", "Sleeve length", DesignSelectionMode.SingleChoice, true,
            Sheets.BlouseSleeve, SleeveStyleSix(withImpact: false)));
        groups.Add(new SeededGroup(Gown, "sleeve_shape", "Sleeve shape", DesignSelectionMode.SingleChoice, false,
            Sheets.BlouseSleeve, SleeveShapeFour(withImpact: false)));
        groups.Add(new SeededGroup(Gown, "gown_slit", "Slit", DesignSelectionMode.SingleChoice, true,
            Sheets.GownSilhouette,
        [
            new("NONE", "No slit"),
            new("SIDE_LOW", "Low side slit", "PI_GOWN_SLIT_LOW"),
            new("THIGH_HIGH", "Thigh-high slit", "PI_GOWN_SLIT_HIGH"),
            new("FRONT_CENTRE", "Centre front slit", "PI_GOWN_SLIT_FRONT"),
        ]));
        groups.Add(new SeededGroup(Gown, "trail", "Trail", DesignSelectionMode.SingleChoice, true,
            Sheets.GownSilhouette,
        [
            new("NONE", "No trail"),
            new("SHORT_TRAIL", "Short trail", "PI_GOWN_TRAIL_SHORT"),
            new("LONG_TRAIL", "Long trail", "PI_GOWN_TRAIL_LONG", 2),
        ]));
        groups.Add(new SeededGroup(Gown, "closure", "Closure", DesignSelectionMode.SingleChoice, true,
            Sheets.BlouseClosure,
        [
            new("CONCEALED_ZIP", "Concealed zip", "PI_GOWN_ZIP"),
            new("ZIP_BACK", "Exposed zip, back"),
            new("LACE_UP", "Lace-up back", "PI_GOWN_LACE_UP", 1),
            new("HOOK", "Hooks"),
        ]));
        groups.Add(new SeededGroup(Gown, "lining", "Lining", DesignSelectionMode.SingleChoice, true,
            Sheets.GownSilhouette,
        [
            new("NONE", "No lining"),
            new("BODICE_ONLY", "Bodice lined", "PI_GOWN_LINING_BODICE"),
            new("FULL", "Fully lined", "PI_GOWN_LINING_FULL"),
        ]));
        groups.Add(new SeededGroup(Gown, "padding", "Cup and support", DesignSelectionMode.SingleChoice, false,
            Sheets.GownSilhouette,
        [
            new("NONE", "No cup or support"),
            new("LIGHT", "Light padding", "PI_GOWN_PADDING"),
            new("MOULDED_CUP", "Moulded cup", "PI_GOWN_PADDING_CUP"),
            new("BONED_BODICE", "Boned bodice", "PI_GOWN_BONED_BODICE", 1),
        ]));
        groups.Add(new SeededGroup(Gown, "edge_finish", "Edge finish", DesignSelectionMode.MultipleChoice, false,
            Sheets.BlouseFinish,
        [
            new("BOUND_EDGE", "Bound edge", "PI_GOWN_BOUND_EDGE"),
            new("PIPING", "Piping", "PI_GOWN_PIPING"),
            new("HORSEHAIR_HEM", "Horsehair braid hem", "PI_GOWN_HORSEHAIR", 1),
        ]));

        // 9.6 KIDS — six groups, deliberately the smallest set.
        groups.Add(new SeededGroup(Kids, "garment_style", "Style", DesignSelectionMode.SingleChoice, true,
            Sheets.KidsStyle,
        [
            new("A_LINE_FROCK", "A-line frock"),
            new("A_LINE_FRILL", "A-line frock with frill"),
            new("GATHERED_FROCK", "Gathered frock"),
            new("PATTU_PAVADAI", "Pattu pavadai", "PI_KIDS_PAVADAI", 1),
            new("KIDS_SALWAR", "Kids salwar set", "PI_KIDS_SALWAR"),
            new("KIDS_GOWN", "Kids gown", "PI_KIDS_GOWN", 1),
        ]));
        groups.Add(new SeededGroup(Kids, "sleeve_style", "Sleeve length", DesignSelectionMode.SingleChoice, true,
            Sheets.KidsStyle,
        [
            new("SLEEVELESS", "Sleeveless"),
            new("CAP", "Cap sleeve"),
            new("SHORT", "Short sleeve"),
            new("FULL", "Full sleeve"),
        ]));
        groups.Add(new SeededGroup(Kids, "sleeve_shape", "Sleeve shape", DesignSelectionMode.SingleChoice, false,
            Sheets.KidsStyle,
        [
            new("PLAIN", "Plain sleeve"),
            new("PUFF", "Puff sleeve"),
            new("BELL", "Bell sleeve"),
            new("FRILL", "Frill sleeve"),
        ]));
        groups.Add(new SeededGroup(Kids, "closure", "Closure", DesignSelectionMode.SingleChoice, true,
            Sheets.KidsStyle,
        [
            new("BACK_BUTTON", "Buttons, back"),
            new("BACK_ZIP", "Zip, back", "PI_KIDS_ZIP"),
            new("TIE_BACK", "Tie back"),
            new("ELASTIC", "Elastic, no opening"),
        ]));
        groups.Add(new SeededGroup(Kids, "lining", "Lining", DesignSelectionMode.SingleChoice, false,
            Sheets.KidsStyle,
        [
            new("NONE", "No lining"),
            new("FULL", "Full lining", "PI_KIDS_LINING"),
        ]));
        groups.Add(new SeededGroup(Kids, "trim", "Trim", DesignSelectionMode.MultipleChoice, false,
            Sheets.KidsStyle,
        [
            new("CONTRAST_FRILL", "Contrast frill", "PI_KIDS_FRILL"),
            new("LACE", "Lace trim", "PI_KIDS_LACE"),
            new("BOW", "Bow"),
            new("POCKETS", "Pockets", "PI_KIDS_POCKETS"),
        ]));

        return groups;
    }

    private static List<SeededRule> BuildRules()
    {
        var rules = new List<SeededRule>();

        // 9.1 BLOUSE_PATTERN
        rules.AddRange(BlouseCoreRules(BlousePattern, 1, 2, 3, 4, 5, 6, 31));

        // 9.2 BLOUSE_AARI — new embroidery rules DR-07 to DR-12, then the copied core rules DR-32 to DR-38.
        rules.Add(new SeededRule(7, BlouseAari, DesignRuleType.Requires,
            new DesignRuleOperand("aari_placement", DesignOperandForm.Includes, ["SLEEVE"]),
            new DesignRuleOperand("sleeve_style", DesignOperandForm.NotEquals, ["SLEEVELESS"]),
            null,
            "There is no sleeve to embroider — the same fact as the design.sleeve_style measurement rule."));
        rules.Add(new SeededRule(8, BlouseAari, DesignRuleType.RequiresAttachment,
            new DesignRuleOperand("aari_motif", DesignOperandForm.Includes, ["CUSTOM_REFERENCE"]),
            null,
            null,
            "The specialist cannot work from a code alone (OD-DES-12)."));
        rules.Add(new SeededRule(9, BlouseAari, DesignRuleType.Note,
            new DesignRuleOperand("aari_density", DesignOperandForm.Equals, ["HEAVY"]),
            null,
            "Confirm with the customer whether the heavy work is stone or thread; thread-only bridal "
            + "work takes longer on the frame.",
            "Density describes coverage, not what covers it (OD-DES-04)."));
        rules.Add(new SeededRule(10, BlouseAari, DesignRuleType.Note,
            new DesignRuleOperand("aari_placement", DesignOperandForm.AnySelection, []),
            null,
            "When trimming the returned panels, do not cut closer than 15 mm to the worked edge.",
            "Printed on the job card and read at stitching, after the worked panels come back."));
        rules.Add(new SeededRule(11, BlouseAari, DesignRuleType.Note,
            new DesignRuleOperand("aari_placement", DesignOperandForm.AnySelection, []),
            null,
            "Back the worked area so knots and stone settings do not sit against the skin.",
            "Whether this should instead force a lining selection is OD-DES-04."));
        rules.Add(new SeededRule(12, BlouseAari, DesignRuleType.Note,
            new DesignRuleOperand("aari_density", DesignOperandForm.Equals, ["HEAVY"]),
            null,
            "Confirm the specialist's capacity before promising the due date.",
            "Heavy work is the most common cause of a missed date on this category."));
        rules.AddRange(BlouseCoreRules(BlouseAari, 32, 33, 34, 35, 36, 37, 38));

        // 9.3 SALWAR
        rules.Add(new SeededRule(13, Salwar, DesignRuleType.Requires,
            new DesignRuleOperand("kameez_slit", DesignOperandForm.Equals, ["FRONT_SLIT"]),
            new DesignRuleOperand("lining", DesignOperandForm.In, ["KAMEEZ_ONLY", "FULL"]),
            null,
            "A front slit exposes the inside of the kameez."));
        // DR-14: the document joins "bottom_style = CHURIDAR" with "lining = FULL"; the same compound
        // condition DR-03 has and the same correction — anchored on the churidar, the condition the
        // craft instruction is actually about, with the lining qualification folded into the note text.
        rules.Add(new SeededRule(14, Salwar, DesignRuleType.Note,
            new DesignRuleOperand("bottom_style", DesignOperandForm.Equals, ["CHURIDAR"]),
            null,
            "Cut the churidar lining to the same ankle length and gather both layers together; allow "
            + "the extra length. Applies when the bottom is lined.",
            "Churidars in net, georgette and light silk are lined as a matter of course (OD-DES-04)."));
        rules.Add(new SeededRule(15, Salwar, DesignRuleType.Note,
            new DesignRuleOperand("bottom_style", DesignOperandForm.Equals, ["PATIALA"]),
            null,
            "Confirm the fabric length covers the pleats before cutting; a patiala takes about 0.5 m more.",
            "The most frequent material shortage on this category."));
        rules.Add(new SeededRule(16, Salwar, DesignRuleType.Note,
            new DesignRuleOperand("dupatta_finish", DesignOperandForm.NotEquals, ["NONE"]),
            null,
            "The dupatta is finished with the set and delivered with it; it is not a separate job.",
            "Distinguishes this from the Lehenga treatment of Section 9.4."));
        rules.Add(new SeededRule(39, Salwar, DesignRuleType.Excludes,
            new DesignRuleOperand("sleeve_shape", DesignOperandForm.NotEquals, ["PLAIN"]),
            new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]),
            null,
            "As DR-31 on the blouse."));

        // 9.4 LEHENGA
        rules.Add(new SeededRule(17, Lehenga, DesignRuleType.Requires,
            new DesignRuleOperand("skirt_style", DesignOperandForm.Equals, ["LAYERED"]),
            new DesignRuleOperand("waist_finish", DesignOperandForm.In, ["HOOK_BAND", "ZIP"]),
            null,
            "A drawstring alone will not hold the weight of a can-can layered skirt."));
        rules.Add(new SeededRule(18, Lehenga, DesignRuleType.Excludes,
            new DesignRuleOperand("skirt_style", DesignOperandForm.Equals, ["MERMAID"]),
            new DesignRuleOperand("waist_finish", DesignOperandForm.Includes, ["ELASTIC_BACK"]),
            null,
            "A mermaid skirt is fitted through the hip and cannot gather at the waist."));
        rules.Add(new SeededRule(19, Lehenga, DesignRuleType.Note,
            new DesignRuleOperand("dupatta", DesignOperandForm.NotEquals, ["NONE"]),
            null,
            "The dupatta is stitched as its own garment job on the same order, with a deliver_together "
            + "dependency.",
            "Why this group carries no price impact of its own (OD-DES-11)."));
        rules.Add(new SeededRule(20, Lehenga, DesignRuleType.Note,
            new DesignRuleOperand("skirt_style", DesignOperandForm.Equals, ["KALI"]),
            null,
            "Confirm the kali count with the Tailor Master against the flare before cutting.",
            "kali_count is a measurement; the two must agree."));
        rules.Add(new SeededRule(40, Lehenga, DesignRuleType.Requires,
            new DesignRuleOperand("padding", DesignOperandForm.In, ["LIGHT", "MOULDED_CUP"]),
            new DesignRuleOperand("lining", DesignOperandForm.NotEquals, ["NONE"]),
            null,
            "The DR-01 analogue on the choli, seeded with its own identifier (rules are category-scoped)."));
        rules.Add(new SeededRule(41, Lehenga, DesignRuleType.Excludes,
            new DesignRuleOperand("sleeve_shape", DesignOperandForm.NotEquals, ["PLAIN"]),
            new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]),
            null,
            "As DR-31 on the blouse."));

        // 9.5 GOWN
        rules.Add(new SeededRule(21, Gown, DesignRuleType.Requires,
            new DesignRuleOperand("gown_slit", DesignOperandForm.NotEquals, ["NONE"]),
            new DesignRuleOperand("edge_finish", DesignOperandForm.Includes, ["BOUND_EDGE"]),
            null,
            "A raw slit edge frays on the first wear."));
        rules.Add(new SeededRule(22, Gown, DesignRuleType.Requires,
            new DesignRuleOperand("gown_slit", DesignOperandForm.NotEquals, ["NONE"]),
            new DesignRuleOperand("lining", DesignOperandForm.NotEquals, ["NONE"]),
            null,
            "An unlined slit shows the seam allowance."));
        rules.Add(new SeededRule(23, Gown, DesignRuleType.Requires,
            new DesignRuleOperand("trail", DesignOperandForm.NotEquals, ["NONE"]),
            new DesignRuleOperand("lining", DesignOperandForm.In, ["FULL"]),
            null,
            "A trail drags on the floor and needs the second layer to hold its shape."));
        rules.Add(new SeededRule(24, Gown, DesignRuleType.Requires,
            new DesignRuleOperand("neckline", DesignOperandForm.Equals, ["OFF_SHOULDER"]),
            new DesignRuleOperand("closure", DesignOperandForm.In, ["CONCEALED_ZIP", "LACE_UP"]),
            null,
            "The bodice must be held without shoulder support."));
        rules.Add(new SeededRule(25, Gown, DesignRuleType.Excludes,
            new DesignRuleOperand("neckline", DesignOperandForm.Equals, ["HALTER"]),
            new DesignRuleOperand("sleeve_style", DesignOperandForm.NotEquals, ["SLEEVELESS"]),
            null,
            "There is no shoulder to hang a sleeve from. 'other than' is section 4's reading of ≠."));
        rules.Add(new SeededRule(26, Gown, DesignRuleType.Note,
            new DesignRuleOperand("trail", DesignOperandForm.Equals, ["LONG_TRAIL"]),
            null,
            "Measure the trail on the customer in the shoes she will wear.",
            "Not in the measurement set; the most common gown remake."));
        rules.Add(new SeededRule(42, Gown, DesignRuleType.Requires,
            new DesignRuleOperand("neckline", DesignOperandForm.In, ["HALTER", "OFF_SHOULDER"]),
            new DesignRuleOperand("padding", DesignOperandForm.NotEquals, ["NONE"]),
            null,
            "Neither neckline has a shoulder to hang from; the bodice carries its own support."));
        rules.Add(new SeededRule(43, Gown, DesignRuleType.Excludes,
            new DesignRuleOperand("sleeve_shape", DesignOperandForm.NotEquals, ["PLAIN"]),
            new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]),
            null,
            "As DR-31 on the blouse."));

        // 9.6 KIDS
        rules.Add(new SeededRule(27, Kids, DesignRuleType.Requires,
            new DesignRuleOperand("garment_style", DesignOperandForm.Equals, ["PATTU_PAVADAI"]),
            new DesignRuleOperand("lining", DesignOperandForm.Equals, ["FULL"]),
            null,
            "Silk against a child's skin is lined as a matter of course."));
        rules.Add(new SeededRule(28, Kids, DesignRuleType.Excludes,
            new DesignRuleOperand("closure", DesignOperandForm.Equals, ["ELASTIC"]),
            new DesignRuleOperand("garment_style", DesignOperandForm.In, ["PATTU_PAVADAI", "KIDS_GOWN"]),
            null,
            "Neither garment can be pulled on over the head."));
        rules.Add(new SeededRule(29, Kids, DesignRuleType.Note,
            DesignRuleOperand.Always,
            null,
            "Apply the published KIDS growth allowance at cutting; do not add it to the measurement.",
            "The ease convention restated where the cutter reads it."));
        rules.Add(new SeededRule(30, Kids, DesignRuleType.Note,
            new DesignRuleOperand("trim", DesignOperandForm.Equals, ["BOW"]),
            null,
            "Stitch the bow down; a tied bow on a young child's garment is a safety hazard.",
            "Safety instruction, always printed."));
        rules.Add(new SeededRule(44, Kids, DesignRuleType.Excludes,
            new DesignRuleOperand("sleeve_shape", DesignOperandForm.NotEquals, ["PLAIN"]),
            new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]),
            null,
            "As DR-31 on the blouse."));

        return rules;
    }
}
