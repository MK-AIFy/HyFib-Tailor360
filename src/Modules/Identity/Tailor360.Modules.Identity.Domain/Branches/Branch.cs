using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Branches;

/// <summary>
/// An operating location: its code, its name, the IANA timezone every due date and report cut-off is
/// computed in, and whether it is open.
/// </summary>
/// <remarks>
/// The branch register lives in Identity because branch scope is an authorisation concept before it is
/// anything else (<c>docs/architecture/module-ownership.md</c> section 5.1). The working calendar and
/// holidays that the due-date and SLA clocks read arrive with issue #25, alongside the screens that
/// edit them; this issue needs the register itself, because a branch assignment must point at something.
/// </remarks>
public sealed class Branch
{
    /// <summary>The longest branch code the store accepts.</summary>
    public const int MaximumCodeLength = 16;

    /// <summary>The longest branch name the store accepts.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest timezone identifier the store accepts.</summary>
    public const int MaximumTimeZoneLength = 64;

    /// <summary>The timezone a branch is created with when none is given.</summary>
    public const string DefaultTimeZoneId = "Asia/Kolkata";

    private Branch()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private Branch(
        Guid id,
        Guid organisationId,
        string code,
        string name,
        string timeZoneId,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        Code = code;
        Name = name;
        TimeZoneId = timeZoneId;
        Status = BranchStatus.Active;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>Identity of the branch.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the branch belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The short code that appears in order, estimate and invoice numbers.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The branch name shown on screen and on printed documents.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The IANA timezone identifier, for example <c>Asia/Kolkata</c>.</summary>
    public string TimeZoneId { get; private set; } = DefaultTimeZoneId;

    /// <summary>Whether the branch may be worked in.</summary>
    public BranchStatus Status { get; private set; }

    /// <summary>When the branch was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When the branch last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Opens a branch.</summary>
    /// <param name="id">Identity of the branch, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="code">The short code used in document numbers.</param>
    /// <param name="name">The branch name.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="timeZoneId">The IANA timezone identifier.</param>
    /// <param name="by">The administrator opening it.</param>
    public static Result<Branch> Open(
        Guid id,
        Guid organisationId,
        string? code,
        string? name,
        DateTimeOffset now,
        string? timeZoneId = DefaultTimeZoneId,
        Guid? by = null)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<Branch>(IdentityErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<Branch>(IdentityErrors.Required("organisationId"));
        }

        var normalisedCode = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalisedCode))
        {
            return Result.Failure<Branch>(IdentityErrors.Required("code"));
        }

        if (normalisedCode.Length > MaximumCodeLength)
        {
            return Result.Failure<Branch>(IdentityErrors.TooLong("code", MaximumCodeLength));
        }

        if (!normalisedCode.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)))
        {
            return Result.Failure<Branch>(IdentityErrors.BranchCodeNotWellFormed);
        }

        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return Result.Failure<Branch>(IdentityErrors.Required("name"));
        }

        if (trimmedName.Length > MaximumNameLength)
        {
            return Result.Failure<Branch>(IdentityErrors.TooLong("name", MaximumNameLength));
        }

        var zone = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim();
        if (zone.Length > MaximumTimeZoneLength)
        {
            return Result.Failure<Branch>(IdentityErrors.TooLong("timeZoneId", MaximumTimeZoneLength));
        }

        return new Branch(id, organisationId, normalisedCode, trimmedName, zone, now, by);
    }

    /// <summary>Renames the branch and moves its timezone.</summary>
    public Result Reconfigure(string? name, string? timeZoneId, DateTimeOffset now, Guid? by)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return Result.Failure(IdentityErrors.Required("name"));
        }

        if (trimmedName.Length > MaximumNameLength)
        {
            return Result.Failure(IdentityErrors.TooLong("name", MaximumNameLength));
        }

        var zone = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim();
        if (zone.Length > MaximumTimeZoneLength)
        {
            return Result.Failure(IdentityErrors.TooLong("timeZoneId", MaximumTimeZoneLength));
        }

        Name = trimmedName;
        TimeZoneId = zone;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Closes the branch. Historical references stay resolvable.</summary>
    public void Deactivate(DateTimeOffset now, Guid? by)
    {
        Status = BranchStatus.Inactive;
        Touch(now, by);
    }

    /// <summary>Reopens a closed branch.</summary>
    public void Reactivate(DateTimeOffset now, Guid? by)
    {
        Status = BranchStatus.Active;
        Touch(now, by);
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
