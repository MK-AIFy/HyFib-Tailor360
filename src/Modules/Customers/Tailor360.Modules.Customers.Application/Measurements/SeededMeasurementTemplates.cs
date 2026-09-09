using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.Modules.Customers.Application.Measurements;

/// <summary>
/// The six templates transcribed from <c>docs/prd/measurement-templates.md</c> section 9.
/// </summary>
/// <remarks>
/// <para>
/// Every number here comes from that document and none was invented. The bounds are its hard bounds in
/// millimetres, the warning thresholds its confirmation bands, the inch steps its precisions, and the conditional
/// rules its rule column. Where the document says a field is "required while shown", the field is required and
/// carries a rule; the two together are what that phrase means.
/// </para>
/// <para>
/// The help text states where the tape runs and whether the value is a <em>body</em> or a <em>finished</em>
/// measurement, because section 5 puts the distinction there rather than in a column — open decision
/// <strong>OD-MEA-04</strong> asks whether it should become structured data and proposes keeping it as guidance
/// for launch. Confusing the two is the commonest cause of a re-make, so it is never left to be inferred.
/// </para>
/// <para>
/// The whole set is <strong>proposed and to be confirmed</strong> (OD-MEA-01, OD-MEA-07, OD-MEA-08). It is seeded
/// as drafts for exactly that reason.
/// </para>
/// </remarks>
public static class SeededMeasurementTemplates
{
    /// <summary>Every template the seeder writes.</summary>
    public static IReadOnlyList<SeededTemplate> All { get; } =
    [
        BlousePattern(),
        BlouseAari(),
        Salwar(),
        Lehenga(),
        Gown(),
        Kids(),
    ];

    private static ConditionalRule Sleeveless() => new(
        RuleEffect.HiddenWhen,
        [new RuleClause(RuleScope.DesignSelection, "sleeve_style", RuleOperator.IsAnyOf, ["SLEEVELESS"])]);

    private static ConditionalRule PlacementExcludes(string placement) => new(
        RuleEffect.HiddenWhen,
        [new RuleClause(RuleScope.DesignSelection, "aari_placement", RuleOperator.Excludes, [placement])]);

    private static ConditionalRule SelectionIs(string group, string code) => new(
        RuleEffect.HiddenWhen,
        [new RuleClause(RuleScope.DesignSelection, group, RuleOperator.IsAnyOf, [code])]);

    private static TemplateFieldDefinition Length(
        string key,
        string label,
        string group,
        int order,
        bool required,
        int minimum,
        int maximum,
        int? warnBelow,
        int? warnAbove,
        FieldPrecision precision,
        string diagram,
        string help,
        ConditionalRule? rule = null)
        => new(
            key,
            label,
            null,
            group,
            order,
            CanonicalUnit.Millimetre,
            precision,
            new ValidationBands(minimum, maximum, warnBelow, warnAbove),
            required,
            help,
            diagram,
            null,
            AltFor(key, help),
            rule,
            []);

    private static TemplateFieldDefinition Counted(
        string key,
        string label,
        string group,
        int order,
        int minimum,
        int maximum,
        int warnBelow,
        int warnAbove,
        string diagram,
        string help)
        => new(
            key,
            label,
            null,
            group,
            order,
            CanonicalUnit.Count,
            FieldPrecision.Whole,
            new ValidationBands(minimum, maximum, warnBelow, warnAbove),
            true,
            help,
            diagram,
            null,
            AltFor(key, help),
            null,
            []);

    private static TemplateFieldDefinition Chosen(
        string key,
        string label,
        string group,
        int order,
        string diagram,
        string help,
        params string[] codes)
        => new(
            key,
            label,
            null,
            group,
            order,
            CanonicalUnit.None,
            FieldPrecision.Whole,
            ValidationBands.None,
            true,
            help,
            diagram,
            null,
            AltFor(key, help),
            null,
            [.. codes.Select((code, index) => new ChoiceOption(code, Humanise(code), null, index))]);

    // The alternative text describes the measuring path in words, which for these fields is what the help text
    // already says (section 8). Bundled line drawings arrive with #31; until then the words are what a screen
    // reader and a printed sheet have, so they are never empty.
    private static string AltFor(string key, string help) => help;

    private static string Humanise(string code)
        => string.Join(
            ' ',
            code.Split('_').Select(part => part.Length switch
            {
                0 => part,
                1 => part,
                _ => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant(),
            }));

    private static SeededTemplate BlousePattern() => new(
        "MT_BLOUSE_PATTERN",
        "Blouse, Pattern",
        "Linked from BLOUSE_PATTERN.STITCHING, .ALTERATION and .RESTITCHING.",
        DisplayUnit.Inch,
        "Version 1",
        "Proposed field set from docs/prd/measurement-templates.md section 9.1, for review with the Tailor Master "
        + "before publication (OD-MEA-01, OD-MEA-07, OD-MEA-08).",
        [
            .. BlouseBodice("Bodice", "Blouse length"),
            .. BlouseSleeve(),
            .. BlouseNeckline(),
            .. BlouseShaping(),
        ]);

    private static SeededTemplate BlouseAari() => new(
        "MT_BLOUSE_AARI",
        "Blouse, Aari work",
        "Linked from BLOUSE_AARI.STITCHING, .ALTERATION and .RESTITCHING. The bodice, sleeve, neckline and "
        + "shaping fields match MT_BLOUSE_PATTERN; the Aari placement group records where the embroidery sits. "
        + "Motif, density and stone type are design options, not measurements.",
        DisplayUnit.Inch,
        "Version 1",
        "Proposed field set from docs/prd/measurement-templates.md section 9.2, for review with the Tailor Master "
        + "before publication (OD-MEA-01, OD-MEA-07, OD-MEA-08).",
        [
            .. BlouseBodice("Bodice", "Blouse length"),
            .. BlouseSleeve(),
            .. BlouseNeckline(),
            .. BlouseShaping(),
            Length("aari_front_work_height", "Front work area height", "Aari placement", 15, true, 50, 800, 80, 600,
                FieldPrecision.Eighths, "blouse_aari_placement_v1",
                "Finished measurement. Down the front of the garment, over the area the embroidery will cover.",
                PlacementExcludes("FRONT")),
            Length("aari_front_work_width", "Front work area width", "Aari placement", 16, true, 50, 600, 80, 450,
                FieldPrecision.Eighths, "blouse_aari_placement_v1",
                "Finished measurement. Across the front, over the area the embroidery will cover.",
                PlacementExcludes("FRONT")),
            Length("aari_back_work_height", "Back work area height", "Aari placement", 17, false, 50, 800, 80, 600,
                FieldPrecision.Eighths, "blouse_aari_placement_v1",
                "Finished measurement. Down the back of the garment, over the embroidered area.",
                PlacementExcludes("BACK")),
            Length("aari_back_work_width", "Back work area width", "Aari placement", 18, false, 50, 600, 80, 450,
                FieldPrecision.Eighths, "blouse_aari_placement_v1",
                "Finished measurement. Across the back, over the embroidered area.",
                PlacementExcludes("BACK")),
            Length("aari_neck_work_depth", "Neck work band depth", "Aari placement", 19, false, 20, 400, 25, 250,
                FieldPrecision.Sixteenths, "blouse_aari_placement_v1",
                "Finished measurement. From the neckline edge down to where the worked band ends.",
                PlacementExcludes("NECK")),
            Length("aari_sleeve_work_length", "Sleeve work band length", "Aari placement", 20, false, 30, 500, 50,
                400, FieldPrecision.Eighths, "blouse_aari_placement_v1",
                "Finished measurement. Along the sleeve, over the length the worked band runs.",
                new ConditionalRule(
                    RuleEffect.HiddenWhen,
                    [
                        new RuleClause(
                            RuleScope.DesignSelection, "sleeve_style", RuleOperator.IsAnyOf, ["SLEEVELESS"]),
                        new RuleClause(
                            RuleScope.DesignSelection, "aari_placement", RuleOperator.Excludes, ["SLEEVE"]),
                    ])),
            Length("aari_border_width", "Hem border work width", "Aari placement", 21, false, 10, 300, 15, 200,
                FieldPrecision.Sixteenths, "blouse_aari_placement_v1",
                "Finished measurement. Up from the hem, over the width of the worked border.",
                PlacementExcludes("BORDER")),
        ]);

    private static SeededTemplate Salwar() => new(
        "MT_SALWAR",
        "Salwar",
        "Linked from SALWAR.STITCHING, .ALTERATION and .RESTITCHING. Covers the kameez and the bottom in one "
        + "template; the bottom style itself is a design option.",
        DisplayUnit.Inch,
        "Version 1",
        "Proposed field set from docs/prd/measurement-templates.md section 9.3, for review with the Tailor Master "
        + "before publication (OD-MEA-01, OD-MEA-03, OD-MEA-07, OD-MEA-08).",
        [
            Length("kameez_length", "Kameez length", "Kameez", 0, true, 600, 1600, 800, 1400,
                FieldPrecision.Eighths, "salwar_kameez_v1",
                "Finished measurement. Shoulder seam at the neck down to the intended kameez hem."),
            Length("shoulder", "Shoulder", "Kameez", 1, true, 250, 560, 320, 480, FieldPrecision.Eighths,
                "salwar_kameez_v1", "Body measurement. Shoulder point to shoulder point across the back."),
            Length("chest_bust", "Chest (bust)", "Kameez", 2, true, 550, 1500, 710, 1270, FieldPrecision.Eighths,
                "salwar_kameez_v1", "Body measurement. Round the fullest part of the bust, tape level at the back."),
            Length("waist", "Waist", "Kameez", 3, true, 450, 1500, 610, 1220, FieldPrecision.Eighths,
                "salwar_kameez_v1", "Body measurement. Round the natural waist."),
            Length("hip", "Hip", "Kameez", 4, true, 550, 1700, 760, 1320, FieldPrecision.Eighths,
                "salwar_kameez_v1", "Body measurement. Round the fullest part of the hip."),
            Length("armhole", "Armhole round", "Kameez", 5, true, 250, 700, 330, 560, FieldPrecision.Eighths,
                "salwar_kameez_v1", "Body measurement. Round the armhole, over the shoulder and under the arm."),
            Length("sleeve_length", "Sleeve length", "Sleeve", 6, true, 40, 800, 100, 650, FieldPrecision.Eighths,
                "salwar_kameez_v1", "Finished measurement. Shoulder point to the intended sleeve hem.",
                Sleeveless()),
            Length("sleeve_round", "Sleeve round at opening", "Sleeve", 7, true, 150, 650, 200, 500,
                FieldPrecision.Eighths, "salwar_kameez_v1",
                "Finished measurement. Round the arm where the sleeve will end.", Sleeveless()),
            Length("front_neck_depth", "Front neck depth", "Neckline", 8, true, 30, 450, 50, 300,
                FieldPrecision.Sixteenths, "salwar_kameez_v1",
                "Finished measurement. Shoulder-seam line at the neck, straight down the front to the neckline "
                + "point."),
            Length("back_neck_depth", "Back neck depth", "Neckline", 9, true, 30, 500, 50, 350,
                FieldPrecision.Sixteenths, "salwar_kameez_v1",
                "Finished measurement. Shoulder-seam line at the neck, straight down the back to the neckline "
                + "point."),
            Length("kameez_slit_height", "Side slit height", "Kameez", 10, false, 0, 700, 100, 500,
                FieldPrecision.Eighths, "salwar_kameez_v1",
                "Finished measurement. Up from the kameez hem to the top of the side slit.",
                SelectionIs("kameez_slit", "NONE")),
            Length("salwar_waist_round", "Bottom waist round", "Bottom", 11, true, 500, 1500, 630, 1220,
                FieldPrecision.Eighths, "salwar_bottom_v1",
                "Body measurement. Round the waist where the bottom will sit."),
            Chosen("waist_finish", "Waist finish", "Bottom", 12, "salwar_bottom_v1",
                "How the bottom is gathered at the waist. Asked with the tape in hand because it drives the cut.",
                "ELASTIC", "DRAWSTRING", "BOTH"),
            Length("elastic_relaxed_length", "Elastic relaxed length", "Bottom", 13, false, 400, 1200, 550, 1000,
                FieldPrecision.Eighths, "salwar_bottom_v1",
                "Finished measurement. The elastic laid flat and unstretched.",
                new ConditionalRule(
                    RuleEffect.ShownWhen,
                    [new RuleClause(RuleScope.Field, "waist_finish", RuleOperator.IsAnyOf, ["ELASTIC", "BOTH"])])),
            Length("salwar_length", "Salwar length", "Bottom", 14, true, 700, 1300, 850, 1150,
                FieldPrecision.Eighths, "salwar_bottom_v1",
                "Finished measurement. Waist down to the intended hem."),
            Length("thigh_round", "Thigh round", "Bottom", 15, true, 350, 900, 450, 800, FieldPrecision.Eighths,
                "salwar_bottom_v1", "Body measurement. Round the fullest part of the thigh."),
            Length("knee_round", "Knee round", "Bottom", 16, true, 250, 700, 320, 600, FieldPrecision.Eighths,
                "salwar_bottom_v1", "Body measurement. Round the knee."),
            Length("bottom_round", "Bottom (ankle) round", "Bottom", 17, true, 200, 700, 250, 560,
                FieldPrecision.Eighths, "salwar_bottom_v1",
                "Finished measurement. Round the opening where the bottom will end."),
        ]);

    private static SeededTemplate Lehenga() => new(
        "MT_LEHENGA",
        "Lehenga",
        "Linked from LEHENGA.STITCHING, .ALTERATION and .RESTITCHING. The choli group repeats the blouse field "
        + "set, relabelled; the skirt and dupatta groups are specific to this category.",
        DisplayUnit.Inch,
        "Version 1",
        "Proposed field set from docs/prd/measurement-templates.md section 9.4, for review with the Tailor Master "
        + "before publication (OD-MEA-01, OD-MEA-07, OD-MEA-08).",
        [
            .. BlouseBodice("Choli", "Choli length"),
            .. BlouseSleeve("Sleeve", withUpperRound: false),
            .. BlouseNeckline(),
            .. BlouseShaping(),
            Length("lehenga_waist", "Lehenga waist", "Skirt", 22, true, 500, 1500, 630, 1220,
                FieldPrecision.Eighths, "lehenga_skirt_v1",
                "Body measurement. Round the waist where the skirt will sit; the ease is added at cutting."),
            Length("hip", "Hip", "Skirt", 23, false, 550, 1700, 760, 1320, FieldPrecision.Eighths,
                "lehenga_skirt_v1", "Body measurement. Round the fullest part of the hip."),
            Length("lehenga_length", "Lehenga length (waist to hem)", "Skirt", 24, true, 700, 1300, 850, 1150,
                FieldPrecision.Eighths, "lehenga_skirt_v1",
                "Finished measurement. Waist down to the intended hem."),
            Length("lehenga_flare", "Flare (hem circumference)", "Skirt", 25, true, 1500, 8000, 2000, 6000,
                FieldPrecision.Eighths, "lehenga_skirt_v1",
                "Finished measurement. Right round the hem of the finished skirt."),
            Counted("kali_count", "Kali count (number of panels)", "Skirt", 26, 4, 24, 6, 16, "lehenga_skirt_v1",
                "How many panels the skirt is cut in. A count, not a length: there is no unit and no conversion."),
            Length("dupatta_length", "Dupatta length", "Dupatta", 27, false, 1800, 3000, 2000, 2800,
                FieldPrecision.Eighths, "lehenga_dupatta_v1",
                "Finished measurement. End to end along the dupatta.", SelectionIs("dupatta", "NONE")),
            Length("dupatta_width", "Dupatta width", "Dupatta", 28, false, 700, 1200, 800, 1150,
                FieldPrecision.Eighths, "lehenga_dupatta_v1",
                "Finished measurement. Across the dupatta.", SelectionIs("dupatta", "NONE")),
        ]);

    private static SeededTemplate Gown() => new(
        "MT_GOWN",
        "Gown",
        "Linked from GOWN.STITCHING, .ALTERATION and .RESTITCHING.",
        DisplayUnit.Inch,
        "Version 1",
        "Proposed field set from docs/prd/measurement-templates.md section 9.5, for review with the Tailor Master "
        + "before publication (OD-MEA-01, OD-MEA-07, OD-MEA-08).",
        [
            Length("gown_full_length", "Full length (shoulder to hem)", "Bodice", 0, true, 600, 1800, 900, 1600,
                FieldPrecision.Eighths, "gown_front_v1",
                "Finished measurement. Shoulder seam at the neck down to the intended hem."),
            Length("shoulder", "Shoulder", "Bodice", 1, true, 250, 560, 320, 480, FieldPrecision.Eighths,
                "gown_front_v1", "Body measurement. Shoulder point to shoulder point across the back."),
            Length("chest_bust", "Chest (bust)", "Bodice", 2, true, 550, 1500, 710, 1270, FieldPrecision.Eighths,
                "gown_front_v1", "Body measurement. Round the fullest part of the bust, tape level at the back."),
            Length("waist", "Waist", "Bodice", 3, true, 450, 1500, 610, 1220, FieldPrecision.Eighths,
                "gown_front_v1", "Body measurement. Round the natural waist."),
            Length("hip", "Hip", "Bodice", 4, true, 550, 1700, 760, 1320, FieldPrecision.Eighths, "gown_front_v1",
                "Body measurement. Round the fullest part of the hip."),
            Length("armhole", "Armhole round", "Bodice", 5, true, 250, 700, 330, 560, FieldPrecision.Eighths,
                "gown_front_v1", "Body measurement. Round the armhole, over the shoulder and under the arm."),
            Length("sleeve_length", "Sleeve length", "Sleeve", 6, true, 40, 800, 100, 650, FieldPrecision.Eighths,
                "gown_front_v1", "Finished measurement. Shoulder point to the intended sleeve hem.", Sleeveless()),
            Length("sleeve_round", "Sleeve round at opening", "Sleeve", 7, true, 150, 650, 200, 500,
                FieldPrecision.Eighths, "gown_front_v1",
                "Finished measurement. Round the arm where the sleeve will end.", Sleeveless()),
            Length("front_neck_depth", "Front neck depth", "Neckline", 8, true, 30, 450, 50, 300,
                FieldPrecision.Sixteenths, "gown_front_v1",
                "Finished measurement. Shoulder-seam line at the neck, straight down the front to the neckline "
                + "point."),
            Length("back_neck_depth", "Back neck depth", "Neckline", 9, true, 30, 500, 50, 350,
                FieldPrecision.Sixteenths, "gown_front_v1",
                "Finished measurement. Shoulder-seam line at the neck, straight down the back to the neckline "
                + "point."),
            Length("gown_flare", "Flare (hem circumference)", "Silhouette", 10, true, 800, 6000, 1200, 4500,
                FieldPrecision.Eighths, "gown_front_v1",
                "Finished measurement. Right round the hem of the finished gown."),
            Length("slit_height", "Slit height from hem", "Silhouette", 11, false, 0, 1200, 100, 900,
                FieldPrecision.Eighths, "gown_front_v1",
                "Finished measurement. Up from the hem to the top of the slit.", SelectionIs("gown_slit", "NONE")),
        ]);

    private static SeededTemplate Kids() => new(
        "MT_KIDS",
        "Kids",
        "Linked from KIDS.STITCHING, .ALTERATION and .RESTITCHING. Deliberately short: a child rarely stands "
        + "still for fourteen measurements.",
        DisplayUnit.Inch,
        "Version 1",
        "Proposed field set from docs/prd/measurement-templates.md section 9.6, for review with the Tailor Master "
        + "before publication. The age-band overlay on the warning bands is a cross-field check for the capture "
        + "wizard (#28) and is not a template rule (OD-MEA-01, OD-MEA-03, OD-MEA-08).",
        [
            Chosen("age_band", "Age band", "Child", 0, "kids_front_v1",
                "Asked first: it drives the growth allowance at cutting and the plausibility warnings.",
                "0_1", "1_2", "2_4", "4_6", "6_8", "8_10", "10_12", "12_14"),
            Length("height", "Height", "Child", 1, true, 500, 1700, 600, 1600, FieldPrecision.Eighths,
                "kids_front_v1", "Body measurement. Standing straight, heel to the top of the head."),
            Length("chest_bust", "Chest", "Body", 2, true, 350, 1100, 450, 950, FieldPrecision.Eighths,
                "kids_front_v1", "Body measurement. Round the fullest part of the chest."),
            Length("waist", "Waist", "Body", 3, true, 300, 1100, 400, 900, FieldPrecision.Eighths, "kids_front_v1",
                "Body measurement. Round the natural waist."),
            Length("shoulder", "Shoulder", "Body", 4, true, 150, 450, 180, 400, FieldPrecision.Eighths,
                "kids_front_v1", "Body measurement. Shoulder point to shoulder point across the back."),
            Length("garment_length", "Garment length", "Garment", 5, true, 200, 1400, 300, 1200,
                FieldPrecision.Eighths, "kids_front_v1",
                "Finished measurement. Shoulder seam at the neck down to the intended hem."),
            Length("sleeve_length", "Sleeve length", "Sleeve", 6, true, 40, 650, 80, 550, FieldPrecision.Eighths,
                "kids_front_v1", "Finished measurement. Shoulder point to the intended sleeve hem.", Sleeveless()),
            Length("sleeve_round", "Sleeve round at opening", "Sleeve", 7, false, 100, 450, 130, 380,
                FieldPrecision.Eighths, "kids_front_v1",
                "Finished measurement. Round the arm where the sleeve will end.", Sleeveless()),
            Length("front_neck_depth", "Front neck depth", "Neckline", 8, false, 20, 300, 30, 200,
                FieldPrecision.Sixteenths, "kids_front_v1",
                "Finished measurement. Shoulder-seam line at the neck, straight down the front to the neckline "
                + "point."),
        ]);

    // The blouse bodice, sleeve, neckline and shaping groups appear in three templates with the same keys, the
    // same meanings and the same bounds. Writing them out per template would be three chances for one of the
    // twelve numbers to be transcribed differently in one place.
    private static IReadOnlyList<TemplateFieldDefinition> BlouseBodice(string group, string lengthLabel) =>
    [
        Length("blouse_full_length", lengthLabel, group, 0, true, 250, 900, 330, 520, FieldPrecision.Eighths,
            "blouse_front_v1",
            "Finished measurement. Shoulder seam at the neck down to the intended garment hem."),
        Length("shoulder", "Shoulder", group, 1, true, 250, 560, 320, 480, FieldPrecision.Eighths,
            "blouse_back_v1", "Body measurement. Shoulder point to shoulder point across the back."),
        Length("upper_chest", "Upper chest", group, 2, true, 500, 1400, 680, 1200, FieldPrecision.Eighths,
            "blouse_front_v1", "Body measurement. Round the chest above the bust, under the armholes."),
        Length("chest_bust", "Chest (bust)", group, 3, true, 550, 1500, 710, 1270, FieldPrecision.Eighths,
            "blouse_front_v1", "Body measurement. Round the fullest part of the bust, tape level at the back."),
        Length("waist", "Waist", group, 4, true, 450, 1500, 610, 1220, FieldPrecision.Eighths, "blouse_front_v1",
            "Body measurement. Round the natural waist."),
        Length("armhole", "Armhole round", group, 5, true, 250, 700, 330, 560, FieldPrecision.Eighths,
            "blouse_sleeve_v1", "Body measurement. Round the armhole, over the shoulder and under the arm."),
    ];

    private static List<TemplateFieldDefinition> BlouseSleeve(
        string group = "Sleeve",
        bool withUpperRound = true)
    {
        var fields = new List<TemplateFieldDefinition>
        {
            Length("sleeve_length", "Sleeve length", group, 6, true, 40, 800, 100, 650, FieldPrecision.Eighths,
                "blouse_sleeve_v1", "Finished measurement. Shoulder point to the intended sleeve hem.",
                Sleeveless()),
        };

        if (withUpperRound)
        {
            fields.Add(Length(
                "sleeve_upper_round", "Sleeve upper round (bicep)", group, 7, false, 200, 700, 230, 550,
                FieldPrecision.Eighths, "blouse_sleeve_v1",
                "Body measurement. Round the widest part of the upper arm.", Sleeveless()));
        }

        fields.Add(Length(
            "sleeve_round", "Sleeve round at opening", group, 8, true, 150, 650, 200, 500, FieldPrecision.Eighths,
            "blouse_sleeve_v1", "Finished measurement. Round the arm where the sleeve will end.", Sleeveless()));

        return fields;
    }

    private static IReadOnlyList<TemplateFieldDefinition> BlouseNeckline() =>
    [
        Length("front_neck_depth", "Front neck depth", "Neckline", 9, true, 30, 450, 50, 300,
            FieldPrecision.Sixteenths, "blouse_front_v1",
            "Finished measurement. Shoulder-seam line at the neck, straight down the front to the neckline point."),
        Length("back_neck_depth", "Back neck depth", "Neckline", 10, true, 30, 500, 50, 350,
            FieldPrecision.Sixteenths, "blouse_back_v1",
            "Finished measurement. Shoulder-seam line at the neck, straight down the back to the neckline point."),
    ];

    private static IReadOnlyList<TemplateFieldDefinition> BlouseShaping() =>
    [
        Length("cross_front", "Cross front", "Shaping", 11, true, 200, 500, 280, 430, FieldPrecision.Sixteenths,
            "blouse_front_v1", "Body measurement. Across the front, armhole crease to armhole crease."),
        Length("cross_back", "Cross back", "Shaping", 12, true, 200, 520, 300, 450, FieldPrecision.Sixteenths,
            "blouse_back_v1", "Body measurement. Across the back, armhole crease to armhole crease."),
        Length("dart_point", "Dart point (shoulder to apex)", "Shaping", 13, true, 120, 400, 180, 330,
            FieldPrecision.Sixteenths, "blouse_front_v1", "Body measurement. Shoulder point down to the bust apex."),
        Length("apex_to_apex", "Apex to apex", "Shaping", 14, true, 100, 350, 150, 280, FieldPrecision.Sixteenths,
            "blouse_front_v1", "Body measurement. Apex to apex across the bust."),
    ];
}

/// <summary>One template the seeder writes, and the fields of its first draft.</summary>
/// <param name="Code">The stable machine key.</param>
/// <param name="Name">What administrators read.</param>
/// <param name="Description">What it is for, and which catalogue services link to it.</param>
/// <param name="DefaultDisplayUnit">The unit the wizard opens in (inches for every seeded template, OD-MEA-06).</param>
/// <param name="VersionName">What the first draft is called.</param>
/// <param name="Notes">What an administrator reviewing the draft needs to know.</param>
/// <param name="Fields">The field set.</param>
public sealed record SeededTemplate(
    string Code,
    string Name,
    string Description,
    DisplayUnit DefaultDisplayUnit,
    string VersionName,
    string Notes,
    IReadOnlyList<TemplateFieldDefinition> Fields);
