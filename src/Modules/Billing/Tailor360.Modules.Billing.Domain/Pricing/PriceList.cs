using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>
/// A price list: the thing whose versions carry the rates. Most shops have one; a shop pricing two
/// branches differently has two, and a branch is priced by at most one published version at a time.
/// </summary>
public sealed class PriceList
{
    /// <summary>The longest name accepted.</summary>
    public const int MaximumNameLength = 120;

    private PriceList()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private PriceList(Guid id, Guid organisationId, string code, string name, DateTimeOffset now, Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        Code = code;
        Name = name.Trim();
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>This price list.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The stable business key, as walkthroughs write <c>PL_CBE01</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What the list is called.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>When it was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Creates a price list.</summary>
    public static Result<PriceList> Create(Guid id, Guid organisationId, string code, string name, DateTimeOffset now, Guid? by)
    {
        if (!BillingCode.IsWellFormed(code))
        {
            return Result.Failure<PriceList>(BillingErrors.CodeNotWellFormed("code"));
        }

        var named = CheckName(name);

        return named.IsFailure
            ? Result.Failure<PriceList>(named.Error)
            : Result.Success(new PriceList(id, organisationId, code, name, now, by));
    }

    /// <summary>Renames the list. Its code never changes: exports and seeds refer to it.</summary>
    public Result Rename(string name, DateTimeOffset now, Guid? by)
    {
        var named = CheckName(name);
        if (named.IsFailure)
        {
            return named;
        }

        Name = name.Trim();
        UpdatedAt = now;
        UpdatedBy = by;

        return Result.Success();
    }

    private static Result CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(BillingErrors.Required("name"));
        }

        return name.Trim().Length > MaximumNameLength
            ? Result.Failure(BillingErrors.TooLong("name", MaximumNameLength))
            : Result.Success();
    }
}
