namespace Tailor360.Modules.Customers.Domain.Deduplication;

/// <summary>How strongly one record resembles another.</summary>
/// <remarks>
/// <para>
/// Three named bands rather than a number out of a hundred. A weighted score would be a set of
/// invented constants — and this repository's rule is that a number with no source is a product
/// decision, not an engineering one (<c>CLAUDE.md</c> section 8). The bands below are derived from
/// what the reasons <em>mean</em>, so each one can be argued in a sentence, and the boundaries are
/// still open for the business to move: they are recorded as proposed in
/// <c>docs/customers/name-normalisation.md</c>.
/// </para>
/// <para>
/// Nothing in the system acts on a band by itself. A high-confidence match is shown to a person with
/// its reasons, and creating the second record anyway is a decision they take and the trail records.
/// </para>
/// </remarks>
public enum DuplicateConfidence
{
    /// <summary>Nothing in common worth showing.</summary>
    None = 0,

    /// <summary>
    /// Worth a glance. The names match but nothing else does, which in a town where a hundred people
    /// share a name is thin evidence on its own.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Worth reading before creating a second record: the name matches and so does where they live.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Almost certainly the same person or the same household. A shared telephone number is the
    /// strongest signal this shop has, and it is the one Reception is searching by anyway.
    /// </summary>
    High = 3,
}
