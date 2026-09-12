using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>Everything an administrator says about one rule.</summary>
/// <param name="Type">Which of the four types it is.</param>
/// <param name="Antecedent">What has to hold for the rule to fire.</param>
/// <param name="Consequent">
/// The option set a <c>requires</c> rule obliges or an <c>excludes</c> rule forbids; null for the
/// other two types, whose consequence is a reference image or a note rather than an option.
/// </param>
/// <param name="Note">The standing instruction a <c>note</c> attaches; optional on the other types.</param>
/// <param name="Why">The reason the rule exists, for the administrator who meets it later.</param>
public sealed record DesignRuleDetails(
    DesignRuleType Type,
    DesignRuleOperand Antecedent,
    DesignRuleOperand? Consequent,
    string? Note,
    string? Why)
{
    /// <summary>The longest note or reason accepted.</summary>
    public const int MaximumTextLength = 500;

    /// <summary>Checks the details that need no other record to check.</summary>
    /// <returns>Success, or the first thing wrong.</returns>
    public Result Validate()
    {
        if (!Enum.IsDefined(Type))
        {
            return Result.Failure(CatalogErrors.Required("type"));
        }

        var antecedent = Antecedent.Validate("antecedent");
        if (antecedent.IsFailure)
        {
            return antecedent;
        }

        if (Type is DesignRuleType.Requires or DesignRuleType.Excludes)
        {
            if (Consequent is null)
            {
                return Result.Failure(CatalogErrors.ConsequentRequired("consequent"));
            }

            var consequent = Consequent.Validate("consequent");
            if (consequent.IsFailure)
            {
                return consequent;
            }

            if (!Consequent.IsOptionSet)
            {
                return Result.Failure(CatalogErrors.OperandMalformed(
                    "consequent",
                    "A requires or excludes rule names the options it obliges or forbids over one "
                    + "group — 'group = OPTION', 'group ≠ OPTION', 'group in (A, B)', 'group includes "
                    + "OPTION' or 'group excludes OPTION' — never 'always' or 'any selection'."));
            }

            if (Antecedent.Form != DesignOperandForm.Always
                && string.Equals(Antecedent.GroupCode, Consequent.GroupCode, StringComparison.Ordinal)
                && Antecedent.OptionCodes.Intersect(Consequent.OptionCodes, StringComparer.Ordinal).Any())
            {
                return Result.Failure(CatalogErrors.OperandMalformed(
                    "consequent", "The two sides of a rule name the same option."));
            }
        }
        else if (Consequent is not null)
        {
            return Result.Failure(CatalogErrors.ConsequentNotAllowed("consequent"));
        }

        if (Type == DesignRuleType.Note && string.IsNullOrWhiteSpace(Note))
        {
            return Result.Failure(CatalogErrors.Required("note"));
        }

        if (Note is { Length: > MaximumTextLength })
        {
            return Result.Failure(CatalogErrors.TooLong("note", MaximumTextLength));
        }

        return Why is { Length: > MaximumTextLength }
            ? Result.Failure(CatalogErrors.TooLong("why", MaximumTextLength))
            : Result.Success();
    }

    /// <summary>Every group code the rule reads.</summary>
    public IEnumerable<string> GroupCodes
        => new[] { Antecedent.GroupCode, Consequent?.GroupCode }
            .Where(code => code is not null)
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal);
}
