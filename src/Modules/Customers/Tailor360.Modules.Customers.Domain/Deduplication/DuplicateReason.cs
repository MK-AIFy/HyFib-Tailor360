namespace Tailor360.Modules.Customers.Domain.Deduplication;

/// <summary>Why one customer record looks like another.</summary>
/// <remarks>
/// The reasons are the point. Issue #26 requires that a duplicate suggestion <em>explain</em> the
/// match and that a person decide — so the output of scoring is a list of these, which a screen turns
/// into a sentence, and never a bare number somebody has to trust.
/// </remarks>
public enum DuplicateReason
{
    /// <summary>The two records share a telephone number, in either position.</summary>
    SharedTelephoneNumber = 0,

    /// <summary>The two names fold to the same search key.</summary>
    SameFoldedName = 1,

    /// <summary>The two Tamil-script names are identical.</summary>
    SameNativeName = 2,

    /// <summary>The folded names share every word, in some order.</summary>
    SameWordsInAnotherOrder = 3,

    /// <summary>The two records give the same area or town.</summary>
    SameLocality = 4,

    /// <summary>The two records give the same postal code.</summary>
    SamePostcode = 5,
}
