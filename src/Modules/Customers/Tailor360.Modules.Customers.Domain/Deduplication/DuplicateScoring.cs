namespace Tailor360.Modules.Customers.Domain.Deduplication;

/// <summary>
/// Decides how strongly two customer records resemble each other, and says why.
/// </summary>
/// <remarks>
/// <para>
/// A pure function over two <see cref="DuplicateSubject"/> values. It never merges anything, never
/// refuses anything and never writes anything: it produces a confidence band and the reasons behind
/// it, and a person decides. Issue #26 is explicit that duplicate suggestions explain the match and
/// require an authorised decision, and the reason is worth restating — a telephone number in this
/// shop is shared by a household, given by a customer as her husband's, and reassigned by the
/// operator eighteen months later. Any of those would make an automatic merge wrong.
/// </para>
/// </remarks>
public static class DuplicateScoring
{
    /// <summary>Compares a record being created with one that already exists.</summary>
    /// <param name="incoming">The record somebody is about to create.</param>
    /// <param name="existing">A record already held.</param>
    /// <returns>The confidence and the reasons for it. Reasons are ordered strongest first.</returns>
    public static DuplicateMatch Compare(DuplicateSubject incoming, DuplicateSubject existing)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(existing);

        var reasons = new List<DuplicateReason>();

        if (SharesATelephoneNumber(incoming, existing))
        {
            reasons.Add(DuplicateReason.SharedTelephoneNumber);
        }

        var sameName = incoming.NormalisedName.Length > 0
            && string.Equals(incoming.NormalisedName, existing.NormalisedName, StringComparison.Ordinal);

        if (sameName)
        {
            reasons.Add(DuplicateReason.SameFoldedName);
        }
        else if (SameWordsInAnotherOrder(incoming.NormalisedName, existing.NormalisedName))
        {
            // "Raman Kavitha" and "Kavitha Raman" are one person written two ways, which happens
            // whenever a form asks for a surname and the person gives their father's name first.
            reasons.Add(DuplicateReason.SameWordsInAnotherOrder);
        }

        if (Same(incoming.NativeName, existing.NativeName))
        {
            reasons.Add(DuplicateReason.SameNativeName);
        }

        if (Same(incoming.Locality, existing.Locality))
        {
            reasons.Add(DuplicateReason.SameLocality);
        }

        if (Same(incoming.Postcode, existing.Postcode))
        {
            reasons.Add(DuplicateReason.SamePostcode);
        }

        return new DuplicateMatch(Confidence(reasons), reasons);
    }

    /// <summary>
    /// The band the reasons add up to.
    /// </summary>
    /// <remarks>
    /// Read it as three sentences rather than as arithmetic. A shared telephone number is the shop's
    /// strongest signal and stands alone. A matching name plus a matching place is strong enough to
    /// read before creating a second record. A matching name on its own is worth a glance in a town
    /// where a hundred people share one.
    /// </remarks>
    private static DuplicateConfidence Confidence(List<DuplicateReason> reasons)
    {
        if (reasons.Contains(DuplicateReason.SharedTelephoneNumber))
        {
            return DuplicateConfidence.High;
        }

        var nameMatches = reasons.Contains(DuplicateReason.SameFoldedName)
            || reasons.Contains(DuplicateReason.SameNativeName)
            || reasons.Contains(DuplicateReason.SameWordsInAnotherOrder);

        if (!nameMatches)
        {
            // A shared address with no shared name is a household, a hostel or a block of flats. It is
            // not a duplicate, and offering it as one would teach the counter to dismiss the warning.
            return DuplicateConfidence.None;
        }

        var placeMatches = reasons.Contains(DuplicateReason.SameLocality)
            || reasons.Contains(DuplicateReason.SamePostcode);

        return placeMatches ? DuplicateConfidence.Medium : DuplicateConfidence.Low;
    }

    private static bool SharesATelephoneNumber(DuplicateSubject first, DuplicateSubject second)
    {
        // Either number against either number: a customer who gave her own number last year and her
        // husband's this year is the same customer.
        foreach (var mine in new[] { first.PhoneE164, first.AlternatePhoneE164 })
        {
            if (string.IsNullOrEmpty(mine))
            {
                continue;
            }

            if (string.Equals(mine, second.PhoneE164, StringComparison.Ordinal)
                || string.Equals(mine, second.AlternatePhoneE164, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameWordsInAnotherOrder(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0)
        {
            return false;
        }

        var mine = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var theirs = second.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (mine.Length < 2 || mine.Length != theirs.Length)
        {
            return false;
        }

        Array.Sort(mine, StringComparer.Ordinal);
        Array.Sort(theirs, StringComparer.Ordinal);

        return mine.SequenceEqual(theirs, StringComparer.Ordinal);
    }

    private static bool Same(string? first, string? second)
        => !string.IsNullOrEmpty(first)
            && !string.IsNullOrEmpty(second)
            && string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}

/// <summary>How strongly two records resemble each other, and why.</summary>
/// <param name="Confidence">The band.</param>
/// <param name="Reasons">
/// What matched, strongest first. Empty when nothing did. A screen turns these into a sentence; they
/// are never shown as codes.
/// </param>
public sealed record DuplicateMatch(
    DuplicateConfidence Confidence,
    IReadOnlyList<DuplicateReason> Reasons);
