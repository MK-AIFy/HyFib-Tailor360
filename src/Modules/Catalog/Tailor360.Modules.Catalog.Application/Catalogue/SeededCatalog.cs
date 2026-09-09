namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// The initial hierarchy, transcribed from <c>docs/prd/category-hierarchy.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Sections 2 and 3 of that document are the source, and this file is a transcription of them rather
/// than a design of its own. Codes, labels, Tamil labels, descriptions and expected durations are
/// copied; nothing here is invented, and a value that has no source in the document is not here.
/// </para>
/// <para>
/// The expected durations are marked <em>proposed</em> in section 3 — to be confirmed by the owner
/// with the Tailor Master — and they are seeded as proposed. They are a default due-date offset in
/// working days against the branch calendar, never a promise to a customer, and an administrator
/// changes one without a deployment.
/// </para>
/// </remarks>
public static class SeededCatalog
{
    /// <summary>The name the seeded draft is given.</summary>
    public const string DraftName = "Initial stitching hierarchy";

    /// <summary>The notes the seeded draft carries, which say what still has to happen to it.</summary>
    public const string DraftNotes =
        "Seeded from docs/prd/category-hierarchy.md sections 2 and 3 by init-reference-data. It is a "
        + "draft and nothing is orderable until it is published. Before publishing: set branch "
        + "availability on each category and service type (an empty set means offered nowhere), and "
        + "supply the five links each service type carries — the measurement template (#27), the "
        + "workflow definition (#33), the design option groups (#30), the price-list item (#41) and "
        + "the QC checklist (#34). The hierarchy itself is open decision OD-CAT-01 and the per-branch "
        + "question is OD-CAT-04.";

    /// <summary>The seven seeded categories, parents before children.</summary>
    public static IReadOnlyList<SeededCategory> Categories { get; } =
    [
        new("BLOUSE", null, "Blouse", "ரவிக்கை", 0,
            "Saree blouse. A grouping node only: orders are always placed against one of its "
            + "sub-categories, never against BLOUSE itself."),
        new("BLOUSE_PATTERN", "BLOUSE", "Blouse — Pattern",
            "ரவிக்கை — பேட்டர்ன்", 0,
            "Plain and pattern-cut saree blouses: princess cut, katori, paithani, high neck, boat neck "
            + "and similar. Cutting and stitching only; no hand embroidery. The highest-volume "
            + "category."),
        new("BLOUSE_AARI", "BLOUSE", "Blouse — Aari work",
            "ரவிக்கை — ஆரி வேலை", 1,
            "Saree blouses that carry Aari (maggam) hand embroidery. Adds a specialist embroidery phase "
            + "between cutting and stitching, a longer lead time, embroidery-specific placement "
            + "measurements and its own quality criteria for stone, bead and thread security."),
        new("SALWAR", null, "Salwar", "சல்வார்", 1,
            "Salwar kameez sets: kameez plus salwar, churidar, pant or palazzo bottom, optionally with "
            + "a dupatta. Priced and measured as one garment job covering both pieces."),
        new("LEHENGA", null, "Lehenga", "லெஹங்கா", 2,
            "Lehenga sets: a flared kali skirt with a fitted choli and an optional dupatta. Bridal and "
            + "festive work; the longest lead times and the highest material value."),
        new("GOWN", null, "Gown", "கவுன்", 3,
            "Floor-length and calf-length gowns, including A-line, flared and fitted silhouettes, with "
            + "optional slit, lining and trail."),
        new("KIDS", null, "Kids", "குழந்தைகள் ஆடை", 4,
            "Children's garments (frocks, pattu pavadai, kids salwar, kids gowns) up to the 14-year age "
            + "band. Simplified measurement set with an age band, and growth allowance applied at "
            + "cutting."),
    ];

    /// <summary>The three service types every orderable category offers.</summary>
    /// <remarks>
    /// A service type is always scoped to one category, so these eighteen records are eighteen
    /// different things with eighteen sets of links, even though they share three codes.
    /// </remarks>
    public static IReadOnlyList<SeededService> Services { get; } =
    [
        new("BLOUSE_PATTERN", "STITCHING", 3, null),
        new("BLOUSE_PATTERN", "ALTERATION", 1, null),
        new("BLOUSE_PATTERN", "RESTITCHING", 2, null),
        new("BLOUSE_AARI", "STITCHING", 10, null),
        new("BLOUSE_AARI", "ALTERATION", 2,
            "An alteration crossing an embroidered area may damage the Aari work. Warn the customer "
            + "before accepting the garment."),
        new("BLOUSE_AARI", "RESTITCHING", 5, null),
        new("SALWAR", "STITCHING", 4, null),
        new("SALWAR", "ALTERATION", 1, null),
        new("SALWAR", "RESTITCHING", 3, null),
        new("LEHENGA", "STITCHING", 12, null),
        new("LEHENGA", "ALTERATION", 2, null),
        new("LEHENGA", "RESTITCHING", 6, null),
        new("GOWN", "STITCHING", 7, null),
        new("GOWN", "ALTERATION", 2, null),
        new("GOWN", "RESTITCHING", 4, null),
        new("KIDS", "STITCHING", 3, null),
        new("KIDS", "ALTERATION", 1, null),
        new("KIDS", "RESTITCHING", 2, null),
    ];

    /// <summary>The label and description each service code carries, whatever category offers it.</summary>
    public static IReadOnlyDictionary<string, SeededServiceKind> Kinds { get; } =
        new Dictionary<string, SeededServiceKind>(StringComparer.Ordinal)
        {
            ["STITCHING"] = new(
                "Stitching",
                0,
                "A new garment cut and stitched from material the customer supplies, or from shop "
                + "material issued against the garment job. A new measurement version is taken, or a "
                + "confirmed one is explicitly reused."),
            ["ALTERATION"] = new(
                "Alteration",
                1,
                "Adjusting an existing finished garment — taking in or letting out, shortening, "
                + "re-fitting a neckline, replacing a closure. Only the fields that change are "
                + "re-measured."),
            ["RESTITCHING"] = new(
                "Re-stitching",
                2,
                "Reconstructing a garment: opening it and re-cutting or re-making it to a new fit or "
                + "pattern, typically re-using the original material. A full new measurement version "
                + "is taken."),
        };
}

/// <summary>One seeded category.</summary>
/// <param name="Code">The machine key.</param>
/// <param name="ParentCode">The parent's code, or null for a top-level category.</param>
/// <param name="Name">The label (en-IN).</param>
/// <param name="NameTamil">The draft Tamil label, to be confirmed in the Tamil glossary review (#19).</param>
/// <param name="DisplayOrder">Where it sits among its siblings.</param>
/// <param name="Description">What the category covers.</param>
public sealed record SeededCategory(
    string Code,
    string? ParentCode,
    string Name,
    string NameTamil,
    int DisplayOrder,
    string Description);

/// <summary>One seeded service type.</summary>
/// <param name="CategoryCode">The category that offers it.</param>
/// <param name="ServiceCode">The service code, unique within that category.</param>
/// <param name="ExpectedDurationDays">The proposed default due-date offset, in working days.</param>
/// <param name="IntakeWarning">What the counter is warned about, where the document names one.</param>
public sealed record SeededService(
    string CategoryCode,
    string ServiceCode,
    int ExpectedDurationDays,
    string? IntakeWarning);

/// <summary>What a service code means, wherever it is offered.</summary>
/// <param name="Name">The label.</param>
/// <param name="DisplayOrder">Where it sits among its category's services.</param>
/// <param name="Description">What the service covers.</param>
public sealed record SeededServiceKind(string Name, int DisplayOrder, string Description);
