using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>What a rule does to the field that carries it.</summary>
public enum RuleEffect
{
    /// <summary>The field is hidden when the rule matches, and shown otherwise.</summary>
    HiddenWhen = 0,

    /// <summary>The field is shown when the rule matches, and hidden otherwise.</summary>
    ShownWhen = 1,
}

/// <summary>Where a clause reads its value from.</summary>
/// <remarks>
/// Two namespaces, and which of them a rule may use is open decision <strong>OD-MEA-02</strong>. The proposed
/// default — implemented here — is that a rule may read the garment's design selections when measurements are
/// captured inside order intake, and treats them as unset otherwise. The recorded fallback, if rules turn out to be
/// allowed to read template fields only, is a seeded <c>has_sleeves</c> field at the head of the Sleeve group;
/// nothing here forecloses it, because a field operand is already the other half of this enum.
/// </remarks>
public enum RuleScope
{
    /// <summary>Another field of the same template version.</summary>
    Field = 0,

    /// <summary>A design selection on the garment, written <c>design.&lt;option_group_code&gt;</c> (#30).</summary>
    DesignSelection = 1,
}

/// <summary>How a clause compares.</summary>
public enum RuleOperator
{
    /// <summary>The operand's value is one of the listed codes.</summary>
    IsAnyOf = 0,

    /// <summary>The operand is a set of selections and none of the listed codes is among them.</summary>
    Excludes = 1,
}

/// <summary>One comparison inside a rule.</summary>
/// <param name="Scope">Where the left-hand side is read from.</param>
/// <param name="Name">The field key, or the design option-group code.</param>
/// <param name="Operator">How it is compared.</param>
/// <param name="Values">The codes compared against.</param>
public sealed record RuleClause(
    RuleScope Scope,
    string Name,
    RuleOperator Operator,
    IReadOnlyList<string> Values);

/// <summary>
/// Whether a field is shown, expressed over other fields and the garment's design selections.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small. The seeded rules are all one effect over a disjunction — "hidden when the sleeve style is
/// sleeveless", "shown when the waist finish is elastic or both" — and a rule language that can express more than
/// the shop needs is a language an administrator can write a bug in without a deployment to catch it. Clauses are
/// combined with <em>or</em> and nothing else; there is no negation, no nesting and no arithmetic.
/// </para>
/// <para>
/// <strong>An undecidable rule shows the field.</strong> When a rule reads a design selection and there are no
/// design selections — measurements taken at the counter outside an order — the field appears and is optional, so
/// nothing is lost (<c>docs/prd/measurement-templates.md</c> section 6). Hiding on missing information would drop a
/// measurement the tailor needs; showing an unnecessary one costs a skipped field.
/// </para>
/// </remarks>
/// <param name="Effect">Whether matching hides or shows.</param>
/// <param name="AnyOf">The clauses, combined with <em>or</em>.</param>
public sealed record ConditionalRule(RuleEffect Effect, IReadOnlyList<RuleClause> AnyOf)
{
    /// <summary>The prefix that marks a design-selection operand.</summary>
    public const string DesignScopePrefix = "design.";

    /// <summary>The most clauses one rule may carry.</summary>
    /// <remarks>
    /// Six is more than any seeded rule needs and few enough that the rule still reads as a sentence on the
    /// administration screen. A rule that needs more is describing something the template should express as a
    /// separate field.
    /// </remarks>
    public const int MaximumClauses = 6;

    /// <summary>
    /// Whether the rule hides its field no matter what anything else says.
    /// </summary>
    /// <remarks>
    /// A rule with no clauses has nothing to match, so <see cref="RuleEffect.HiddenWhen"/> always hides and
    /// <see cref="RuleEffect.ShownWhen"/> never shows. Either way the field can never be captured, which is a
    /// publish-time error when the field is required — the template would demand a value it never asks for.
    /// </remarks>
    public bool HidesUnconditionally => AnyOf.Count == 0;

    /// <summary>The field keys this rule reads.</summary>
    /// <returns>The keys, for the unknown-key and cycle checks.</returns>
    public IEnumerable<string> ReferencedFieldKeys
        => AnyOf.Where(clause => clause.Scope == RuleScope.Field).Select(clause => clause.Name);

    /// <summary>Checks the rule is well formed, before its operands are resolved against the version.</summary>
    /// <param name="key">The field it belongs to, for the problem detail's field name.</param>
    /// <returns>Success, or the first thing wrong with it.</returns>
    public Result Validate(FieldKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (AnyOf.Count > MaximumClauses)
        {
            return Result.Failure(MeasurementErrors.TooManyClauses(key.Value, MaximumClauses));
        }

        foreach (var clause in AnyOf)
        {
            if (clause.Values.Count == 0)
            {
                return Result.Failure(MeasurementErrors.ClauseHasNoValues(key.Value, clause.Name));
            }

            if (clause.Scope == RuleScope.Field && !FieldKey.IsWellFormed(clause.Name))
            {
                return Result.Failure(MeasurementErrors.RuleOperandMalformed(key.Value, clause.Name));
            }

            if (clause.Scope == RuleScope.Field && clause.Name == key.Value)
            {
                // A field whose visibility depends on its own value can never settle: it is hidden, so it has no
                // value, so it is shown, so it has one. Caught here rather than by the cycle check, because a
                // one-node cycle reads as a typo and deserves the clearer message.
                return Result.Failure(MeasurementErrors.RuleReadsItself(key.Value));
            }
        }

        return Result.Success();
    }

    /// <summary>Decides whether the field is shown.</summary>
    /// <param name="values">The values captured so far, by field key.</param>
    /// <param name="selections">The garment's design selections by option-group code, empty outside an order.</param>
    /// <returns>Whether the field is shown, and whether the rule could decide at all.</returns>
    public RuleEvaluation Evaluate(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlySet<string>> selections)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(selections);

        if (HidesUnconditionally)
        {
            return new RuleEvaluation(Effect != RuleEffect.HiddenWhen, Decided: true);
        }

        var decided = true;
        var matched = false;

        foreach (var clause in AnyOf)
        {
            var verdict = Match(clause, values, selections);

            if (verdict is null)
            {
                decided = false;
                continue;
            }

            matched |= verdict.Value;
        }

        // One clause that matched settles the whole disjunction: the clauses are joined by OR, so no
        // operand still unread can turn a true disjunction false. A rule is only genuinely undecided
        // when nothing matched *and* something could not be read — otherwise a capture wizard would
        // make a field optional that a known, matching clause has conclusively shown.
        decided |= matched;

        // An undecidable rule shows the field: see the remarks above.
        if (!decided)
        {
            return new RuleEvaluation(IsShown: true, Decided: false);
        }

        return new RuleEvaluation(Effect == RuleEffect.HiddenWhen ? !matched : matched, Decided: true);
    }

    private static bool? Match(
        RuleClause clause,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlySet<string>> selections)
    {
        if (clause.Scope == RuleScope.Field)
        {
            if (!values.TryGetValue(clause.Name, out var value))
            {
                return null;
            }

            return clause.Operator switch
            {
                RuleOperator.IsAnyOf => clause.Values.Contains(value, StringComparer.Ordinal),
                RuleOperator.Excludes => !clause.Values.Contains(value, StringComparer.Ordinal),
                _ => null,
            };
        }

        if (!selections.TryGetValue(clause.Name, out var selected))
        {
            return null;
        }

        return clause.Operator switch
        {
            RuleOperator.IsAnyOf => clause.Values.Any(selected.Contains),
            RuleOperator.Excludes => !clause.Values.Any(selected.Contains),
            _ => null,
        };
    }
}

/// <summary>What a rule concluded.</summary>
/// <param name="IsShown">Whether the field appears.</param>
/// <param name="Decided">
/// False when an operand was unset, so <paramref name="IsShown"/> is the safe default rather than an answer. The
/// capture wizard uses this to mark the field optional (#28).
/// </param>
public sealed record RuleEvaluation(bool IsShown, bool Decided);
