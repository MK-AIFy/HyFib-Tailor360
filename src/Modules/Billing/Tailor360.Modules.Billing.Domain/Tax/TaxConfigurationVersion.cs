using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Tax;

/// <summary>
/// One version of the organisation's tax configuration: the tax codes in force from a date, with
/// the components and rates each carries (<c>docs/architecture/conventions.md</c> section 1.3).
/// </summary>
/// <remarks>
/// <para>
/// Shaped exactly as the catalogue version is, for the same reason: a calculation is pinned to one
/// version (INV-INV-03), so a change is never an edit of what is in force but a clone, an edit and a
/// second publication. Exactly one version is published at a time; publishing a draft retires the
/// version it supersedes in the same transaction. A published version's rows are frozen by a
/// database trigger as well as by this class.
/// </para>
/// <para>
/// <strong>No statutory number lives here.</strong> The rates, the classifications and whether a
/// cess applies are what the accountant enters; the publication checks say only what would make a
/// calculation contradict itself, such as a CGST without its SGST.
/// </para>
/// </remarks>
public sealed class TaxConfigurationVersion
{
    /// <summary>The longest name accepted.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest notes accepted.</summary>
    public const int MaximumNotesLength = 1000;

    /// <summary>The longest reason accepted.</summary>
    public const int MaximumReasonLength = 500;

    private readonly List<TaxCode> _taxCodes = [];

    private TaxConfigurationVersion()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private TaxConfigurationVersion(
        Guid id,
        Guid organisationId,
        int versionNumber,
        string name,
        string? notes,
        DateOnly effectiveFrom,
        Guid? clonedFromVersionId,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        VersionNumber = versionNumber;
        Name = name.Trim();
        Notes = notes;
        EffectiveFrom = effectiveFrom;
        ClonedFromVersionId = clonedFromVersionId;
        Status = TaxConfigurationStatus.Draft;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>This version.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The number an administrator reads; unique per organisation.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>Where the version stands.</summary>
    public TaxConfigurationStatus Status { get; private set; }

    /// <summary>What the version is called.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Why it exists.</summary>
    public string? Notes { get; private set; }

    /// <summary>The first business day the version applies to, in the branch's calendar.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The version this one was cloned from, when it was.</summary>
    public Guid? ClonedFromVersionId { get; private set; }

    /// <summary>When the version was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When it was published.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Who published it.</summary>
    public Guid? PublishedBy { get; private set; }

    /// <summary>Why it was published.</summary>
    public string? PublishReason { get; private set; }

    /// <summary>When it was retired.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Who retired it.</summary>
    public Guid? RetiredBy { get; private set; }

    /// <summary>Why it was retired.</summary>
    public string? RetiredReason { get; private set; }

    /// <summary>The codes, in code order.</summary>
    public IReadOnlyCollection<TaxCode> TaxCodes => _taxCodes;

    /// <summary>Whether the version may still change.</summary>
    public bool IsEditable => Status == TaxConfigurationStatus.Draft;

    /// <summary>Starts an empty draft.</summary>
    /// <param name="id">Its identifier.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="versionNumber">Its number.</param>
    /// <param name="name">Its name.</param>
    /// <param name="notes">Why it exists.</param>
    /// <param name="effectiveFrom">The first day it applies to.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The draft, or the reason it was refused.</returns>
    public static Result<TaxConfigurationVersion> CreateDraft(
        Guid id,
        Guid organisationId,
        int versionNumber,
        string name,
        string? notes,
        DateOnly effectiveFrom,
        DateTimeOffset now,
        Guid? by)
    {
        var described = Describe(name, notes, effectiveFrom);

        return described.IsFailure
            ? Result.Failure<TaxConfigurationVersion>(described.Error)
            : Result.Success(new TaxConfigurationVersion(
                id, organisationId, versionNumber, name, notes, effectiveFrom, null, now, by));
    }

    /// <summary>Starts a draft holding copies of this version's codes, with the same keys and fresh rows.</summary>
    /// <param name="ids">Where the fresh identifiers come from.</param>
    /// <param name="versionNumber">The draft's number.</param>
    /// <param name="name">Its name.</param>
    /// <param name="notes">Why it exists.</param>
    /// <param name="effectiveFrom">The first day it applies to.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The draft, or the reason it was refused.</returns>
    public Result<TaxConfigurationVersion> CloneAsDraft(
        IIdGenerator ids,
        int versionNumber,
        string name,
        string? notes,
        DateOnly effectiveFrom,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var described = Describe(name, notes, effectiveFrom);
        if (described.IsFailure)
        {
            return Result.Failure<TaxConfigurationVersion>(described.Error);
        }

        var clone = new TaxConfigurationVersion(
            ids.NewId(), OrganisationId, versionNumber, name, notes, effectiveFrom, Id, now, by);
        foreach (var code in _taxCodes)
        {
            clone._taxCodes.Add(code.CopyInto(ids, clone.Id));
        }

        return Result.Success(clone);
    }

    /// <summary>Changes what a draft says about itself.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="notes">Why it exists.</param>
    /// <param name="effectiveFrom">The first day it applies to.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Describe(string name, string? notes, DateOnly effectiveFrom, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(BillingErrors.VersionNotEditable);
        }

        var described = Describe(name, notes, effectiveFrom);
        if (described.IsFailure)
        {
            return described;
        }

        Name = name.Trim();
        Notes = notes;
        EffectiveFrom = effectiveFrom;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>One code by identifier.</summary>
    /// <param name="taxCodeId">The code.</param>
    /// <returns>The code, or null.</returns>
    public TaxCode? FindTaxCode(Guid taxCodeId) => _taxCodes.Find(code => code.Id == taxCodeId);

    /// <summary>One code by its business key.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The code, or null.</returns>
    public TaxCode? FindTaxCodeByCode(string code)
        => _taxCodes.Find(found => string.Equals(found.Code, code, StringComparison.Ordinal));

    /// <summary>Adds a code to a draft.</summary>
    /// <param name="id">The row's identifier.</param>
    /// <param name="key">The concept's identifier.</param>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The code, or the reason it was refused.</returns>
    public Result<TaxCode> AddTaxCode(Guid id, Guid key, TaxCodeDetails details, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<TaxCode>(BillingErrors.VersionNotEditable);
        }

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<TaxCode>(validated.Error);
        }

        if (FindTaxCodeByCode(details.Code) is not null)
        {
            return Result.Failure<TaxCode>(BillingErrors.CodeNotUnique("code"));
        }

        var added = new TaxCode(id, key, Id, OrganisationId, details);
        _taxCodes.Add(added);
        Touch(now, by);

        return Result.Success(added);
    }

    /// <summary>Replaces what a draft says about a code.</summary>
    /// <param name="taxCodeId">The code.</param>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The code, or the reason it was refused.</returns>
    public Result<TaxCode> EditTaxCode(Guid taxCodeId, TaxCodeDetails details, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<TaxCode>(BillingErrors.VersionNotEditable);
        }

        if (FindTaxCode(taxCodeId) is not { } code)
        {
            return Result.Failure<TaxCode>(BillingErrors.TaxCodeNotFound);
        }

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<TaxCode>(validated.Error);
        }

        if (FindTaxCodeByCode(details.Code) is { } other && other.Id != taxCodeId)
        {
            return Result.Failure<TaxCode>(BillingErrors.CodeNotUnique("code"));
        }

        code.Apply(details);
        Touch(now, by);

        return Result.Success(code);
    }

    /// <summary>Removes a code from a draft.</summary>
    /// <param name="taxCodeId">The code.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result RemoveTaxCode(Guid taxCodeId, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(BillingErrors.VersionNotEditable);
        }

        if (FindTaxCode(taxCodeId) is not { } code)
        {
            return Result.Failure(BillingErrors.TaxCodeNotFound);
        }

        _taxCodes.Remove(code);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Publishes a draft. The caller retires the version it supersedes.</summary>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <param name="reason">Why.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Publish(DateTimeOffset now, Guid? by, string reason)
    {
        if (Status != TaxConfigurationStatus.Draft)
        {
            return Result.Failure(BillingErrors.VersionNotPublishable);
        }

        var reasoned = CheckReason(reason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = TaxConfigurationStatus.Published;
        PublishedAt = now;
        PublishedBy = by;
        PublishReason = reason;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Retires the published version, because a successor was published.</summary>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <param name="reason">Why.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Retire(DateTimeOffset now, Guid? by, string reason)
    {
        if (Status != TaxConfigurationStatus.Published)
        {
            return Result.Failure(BillingErrors.VersionNotRetirable);
        }

        var reasoned = CheckReason(reason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = TaxConfigurationStatus.Retired;
        RetiredAt = now;
        RetiredBy = by;
        RetiredReason = reason;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Checks a reason where one is required.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public static Result CheckReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(BillingErrors.ReasonRequired);
        }

        return reason.Length > MaximumReasonLength
            ? Result.Failure(BillingErrors.TooLong("reason", MaximumReasonLength))
            : Result.Success();
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }

    private static Result Describe(string name, string? notes, DateOnly effectiveFrom)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(BillingErrors.Required("name"));
        }

        // An omitted date binds as the first day of year one, which is not a day anything is in
        // force from; a version pinned to it would apply to every calculation ever made.
        if (effectiveFrom == default)
        {
            return Result.Failure(BillingErrors.Required("effectiveFrom"));
        }

        if (name.Length > MaximumNameLength)
        {
            return Result.Failure(BillingErrors.TooLong("name", MaximumNameLength));
        }

        return notes is { Length: > MaximumNotesLength }
            ? Result.Failure(BillingErrors.TooLong("notes", MaximumNotesLength))
            : Result.Success();
    }
}
