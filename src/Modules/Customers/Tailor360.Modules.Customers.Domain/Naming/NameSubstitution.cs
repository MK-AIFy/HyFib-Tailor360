namespace Tailor360.Modules.Customers.Domain.Naming;

/// <summary>
/// One rewrite applied while a name is being reduced to its search key.
/// </summary>
/// <param name="From">The sequence to look for, already lower-cased and unaccented.</param>
/// <param name="To">What it becomes. May be empty, which deletes the sequence.</param>
/// <param name="WordFinalOnly">
/// True when the rule applies only at the end of a word. <c>y</c> becomes <c>i</c> at the end of
/// Lakshmy and must not touch the <c>y</c> in Ayesha.
/// </param>
/// <param name="Because">
/// The pair of real spellings the rule exists to bring together. It is carried in the data rather
/// than in a comment because the table in <c>docs/customers/name-normalisation.md</c> is asserted
/// against this list, and the reason is the half a reviewer needs.
/// </param>
public sealed record NameSubstitution(
    string From,
    string To,
    bool WordFinalOnly,
    string Because);
