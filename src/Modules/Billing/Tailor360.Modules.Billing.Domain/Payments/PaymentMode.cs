using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>
/// A way money is taken: cash, card, UPI, a bank transfer, or another the shop names. Configuration, not
/// a financial record — a keyed register per organisation with an active flag (plan D8,
/// <c>docs/prd/configurable-vs-fixed.md</c>): existing payments keep their mode, and deactivating one
/// stops new use only.
/// </summary>
/// <remarks>
/// Branch availability is a restriction, not an offer: a mode with no branches listed is available at
/// every branch, and one with branches listed is available at those alone. That is the opposite reading
/// from the catalogue's, on purpose — cash must work at a branch nobody has configured yet, and a
/// refusal to take money is the one failure a shop notices immediately.
/// </remarks>
public sealed class PaymentMode
{
    /// <summary>The longest name a mode may carry.</summary>
    public const int MaximumNameLength = 80;

    private readonly List<PaymentModeBranch> _branches = [];

    private PaymentMode()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private PaymentMode(Guid id, Guid organisationId, string code, PaymentModeDetails details, DateTimeOffset now, Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        Code = code;
        Name = details.Name;
        RequiresReference = details.RequiresReference;
        RequiresProvider = details.RequiresProvider;
        AllowedForRefund = details.AllowedForRefund;
        IsActive = true;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The code, upper case, unique per organisation, never changed once defined.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The name on the button.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Whether a payment in this mode must carry an external reference.</summary>
    public bool RequiresReference { get; private set; }

    /// <summary>Whether a payment in this mode goes through a provider intent.</summary>
    public bool RequiresProvider { get; private set; }

    /// <summary>Whether a refund may be paid out through this mode.</summary>
    public bool AllowedForRefund { get; private set; }

    /// <summary>Whether new payments may use it.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The branches it is restricted to; empty means every branch.</summary>
    public IReadOnlyCollection<PaymentModeBranch> Branches => _branches;

    /// <summary>When it was defined.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who defined it; null for the seeder.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>What it says: the mode itself, with the flags an administrator sets.</summary>
    public PaymentModeDetails Details => new(Name, RequiresReference, RequiresProvider, AllowedForRefund);

    /// <summary>Whether a payment may be taken in this mode at a branch today.</summary>
    public bool IsAvailableAt(Guid branchId)
        => IsActive && (_branches.Count == 0 || _branches.Any(branch => branch.BranchId == branchId));

    /// <summary>Defines a mode, active, available everywhere.</summary>
    public static Result<PaymentMode> Define(Guid id, Guid organisationId, string? code, PaymentModeDetails? details, DateTimeOffset now, Guid? by = null)
    {
        var trimmedCode = code?.Trim() ?? string.Empty;
        if (trimmedCode.Length == 0)
        {
            return Result.Failure<PaymentMode>(BillingErrors.Required("code"));
        }

        if (!BillingCode.IsWellFormed(trimmedCode))
        {
            return Result.Failure<PaymentMode>(BillingErrors.CodeNotWellFormed("code"));
        }

        var checkedDetails = CheckDetails(details);
        if (checkedDetails.IsFailure)
        {
            return Result.Failure<PaymentMode>(checkedDetails.Error);
        }

        return Result.Success(new PaymentMode(id, organisationId, trimmedCode, checkedDetails.Value, now, by));
    }

    /// <summary>Changes the name and the flags. True when something moved.</summary>
    public Result<bool> Describe(PaymentModeDetails? details, DateTimeOffset now, Guid? by)
    {
        var checkedDetails = CheckDetails(details);
        if (checkedDetails.IsFailure)
        {
            return Result.Failure<bool>(checkedDetails.Error);
        }

        if (checkedDetails.Value == Details)
        {
            return Result.Success(false);
        }

        Name = checkedDetails.Value.Name;
        RequiresReference = checkedDetails.Value.RequiresReference;
        RequiresProvider = checkedDetails.Value.RequiresProvider;
        AllowedForRefund = checkedDetails.Value.AllowedForRefund;
        Touch(now, by);
        return Result.Success(true);
    }

    /// <summary>Restricts the mode to the branches given, or to none, meaning everywhere. True when the set moved.</summary>
    public bool SetBranches(IReadOnlyCollection<Guid> branchIds, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(branchIds);

        var wanted = branchIds.Distinct().OrderBy(id => id).ToList();
        var current = _branches.Select(branch => branch.BranchId).OrderBy(id => id).ToList();
        if (wanted.SequenceEqual(current))
        {
            return false;
        }

        _branches.Clear();
        _branches.AddRange(wanted.Select(branchId => PaymentModeBranch.For(Id, branchId)));
        Touch(now, by);
        return true;
    }

    /// <summary>Activates or deactivates. True when the flag moved.</summary>
    public bool SetActive(bool active, DateTimeOffset now, Guid? by)
    {
        if (IsActive == active)
        {
            return false;
        }

        IsActive = active;
        Touch(now, by);
        return true;
    }

    private static Result<PaymentModeDetails> CheckDetails(PaymentModeDetails? details)
    {
        var name = details?.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Result.Failure<PaymentModeDetails>(BillingErrors.Required("name"));
        }

        if (name.Length > MaximumNameLength)
        {
            return Result.Failure<PaymentModeDetails>(BillingErrors.TooLong("name", MaximumNameLength));
        }

        return Result.Success(details! with { Name = name });
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
