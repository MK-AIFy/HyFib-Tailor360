using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Branches;

/// <summary>
/// An operating location: its code, its name, the IANA timezone every due date and report cut-off is
/// computed in, and whether it is open.
/// </summary>
/// <remarks>
/// The branch register lives in Identity because branch scope is an authorisation concept before it is
/// anything else (<c>docs/architecture/module-ownership.md</c> section 5.1).
/// <para>
/// The working calendar and holidays are <em>not</em> here, and that is a decision rather than an
/// omission. Nothing reads them until a due date or an SLA clock exists, so defining them now would
/// mean inventing their semantics — whether a promise date rolls forward or back off a holiday,
/// whether half-days exist, whether a holiday can be branch-specific and organisation-wide at once —
/// with no consumer to source the answers from. They arrive with #33, which is the issue that first
/// computes a promise date. The question is recorded in
/// <c>docs/prd/assumptions-and-open-decisions.md</c>.
/// </para>
/// <para>
/// The GST registration reference is a string and not a foreign key. Billing owns the registrations
/// themselves and lives in its own schema, and ARCH-005 forbids a cross-schema key — so this records
/// which registration a branch trades under, and Billing resolves it.
/// </para>
/// </remarks>
public sealed class Branch
{
    /// <summary>The longest branch code the store accepts.</summary>
    public const int MaximumCodeLength = 16;

    /// <summary>The longest branch name the store accepts.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest timezone identifier the store accepts.</summary>
    public const int MaximumTimeZoneLength = 64;

    /// <summary>The longest single line of an address the store accepts.</summary>
    public const int MaximumAddressLineLength = 200;

    /// <summary>The longest contact value the store accepts.</summary>
    public const int MaximumContactLength = 120;

    /// <summary>The longest recorded reason for a change of standing.</summary>
    public const int MaximumStatusReasonLength = 500;

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

    /// <summary>Why the branch was last opened or closed, as the administrator wrote it.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>First line of the street address.</summary>
    public string? AddressLine1 { get; private set; }

    /// <summary>Second line of the street address.</summary>
    public string? AddressLine2 { get; private set; }

    /// <summary>The town or city.</summary>
    public string? City { get; private set; }

    /// <summary>The state.</summary>
    public string? State { get; private set; }

    /// <summary>The postal code.</summary>
    public string? PostalCode { get; private set; }

    /// <summary>The number customers and couriers call.</summary>
    public string? ContactPhone { get; private set; }

    /// <summary>The address customer correspondence comes from.</summary>
    public string? ContactEmail { get; private set; }

    /// <summary>
    /// Which GST registration this branch trades under, as a reference rather than a key.
    /// </summary>
    /// <remarks>Billing owns the registrations; ARCH-005 forbids a key across the schema boundary.</remarks>
    public string? GstRegistrationReference { get; private set; }

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

    /// <summary>Renames the branch, moves its timezone and records its master data.</summary>
    /// <remarks>
    /// The code is not a parameter and never will be. It is embedded in order, estimate and invoice
    /// numbers that are already printed and already quoted back by customers, so changing it would
    /// silently split one branch's history in two. A branch that needs a different code is a different
    /// branch.
    /// </remarks>
    /// <param name="details">The branch's name, timezone, address and contacts.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator making the change.</param>
    public Result Reconfigure(BranchDetails details, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        var trimmedName = details.Name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return Result.Failure(IdentityErrors.Required("name"));
        }

        if (trimmedName.Length > MaximumNameLength)
        {
            return Result.Failure(IdentityErrors.TooLong("name", MaximumNameLength));
        }

        var zone = string.IsNullOrWhiteSpace(details.TimeZoneId) ? DefaultTimeZoneId : details.TimeZoneId.Trim();
        if (zone.Length > MaximumTimeZoneLength)
        {
            return Result.Failure(IdentityErrors.TooLong("timeZoneId", MaximumTimeZoneLength));
        }

        // Checked against the running system's database rather than only for length. Every due date,
        // every report cut-off and every SLA clock for this branch is computed in this zone, so a typo
        // here does not fail — it quietly computes all of them somewhere else.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
        {
            return Result.Failure(IdentityErrors.TimeZoneNotRecognised);
        }

        foreach (var (value, field, maximum) in Bounded(details))
        {
            if (value is { Length: > 0 } && value.Length > maximum)
            {
                return Result.Failure(IdentityErrors.TooLong(field, maximum));
            }
        }

        Name = trimmedName;
        TimeZoneId = zone;
        AddressLine1 = Blank(details.AddressLine1);
        AddressLine2 = Blank(details.AddressLine2);
        City = Blank(details.City);
        State = Blank(details.State);
        PostalCode = Blank(details.PostalCode);
        ContactPhone = Blank(details.ContactPhone);
        ContactEmail = Blank(details.ContactEmail);
        GstRegistrationReference = Blank(details.GstRegistrationReference);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Closes the branch. Nothing is deleted: every order, invoice and assignment that names it stays
    /// resolvable, which is why this is the only way a branch leaves the register.
    /// </summary>
    /// <param name="reason">Why, as the administrator wrote it.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator closing it.</param>
    public Result Deactivate(string? reason, DateTimeOffset now, Guid? by)
    {
        if (Status is BranchStatus.Inactive)
        {
            return Result.Failure(
                IdentityErrors.StatusTransitionNotAllowed(Status.ToString(), nameof(BranchStatus.Inactive)));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(IdentityErrors.ReasonRequired);
        }

        Status = BranchStatus.Inactive;
        StatusReason = Truncated(reason);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Reopens a closed branch.</summary>
    /// <param name="reason">Why, as the administrator wrote it.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator reopening it.</param>
    public Result Reactivate(string? reason, DateTimeOffset now, Guid? by)
    {
        if (Status is BranchStatus.Active)
        {
            return Result.Failure(
                IdentityErrors.StatusTransitionNotAllowed(Status.ToString(), nameof(BranchStatus.Active)));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(IdentityErrors.ReasonRequired);
        }

        Status = BranchStatus.Active;
        StatusReason = Truncated(reason);
        Touch(now, by);

        return Result.Success();
    }

    private static IEnumerable<(string? Value, string Field, int Maximum)> Bounded(BranchDetails details) =>
    [
        (details.AddressLine1, "addressLine1", MaximumAddressLineLength),
        (details.AddressLine2, "addressLine2", MaximumAddressLineLength),
        (details.City, "city", MaximumContactLength),
        (details.State, "state", MaximumContactLength),
        (details.PostalCode, "postalCode", MaximumContactLength),
        (details.ContactPhone, "contactPhone", MaximumContactLength),
        (details.ContactEmail, "contactEmail", MaximumContactLength),
        (details.GstRegistrationReference, "gstRegistrationReference", MaximumContactLength),
    ];

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncated(string reason)
    {
        var trimmed = reason.Trim();

        return trimmed.Length <= MaximumStatusReasonLength
            ? trimmed
            : trimmed[..MaximumStatusReasonLength];
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}

/// <summary>The editable description of a branch.</summary>
/// <remarks>
/// A record rather than eight parameters, so adding a field to the branch does not change the shape of
/// every call site that sets one.
/// </remarks>
/// <param name="Name">The branch name shown on screen and on printed documents.</param>
/// <param name="TimeZoneId">The IANA timezone every due date for this branch is computed in.</param>
/// <param name="AddressLine1">First line of the street address.</param>
/// <param name="AddressLine2">Second line of the street address.</param>
/// <param name="City">The town or city.</param>
/// <param name="State">The state.</param>
/// <param name="PostalCode">The postal code.</param>
/// <param name="ContactPhone">The number customers and couriers call.</param>
/// <param name="ContactEmail">The address customer correspondence comes from.</param>
/// <param name="GstRegistrationReference">Which GST registration the branch trades under.</param>
public sealed record BranchDetails(
    string? Name,
    string? TimeZoneId,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    string? GstRegistrationReference = null);
