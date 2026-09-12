using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Registrations;

/// <summary>
/// A branch's GST registration: the GSTIN and state code printed on every document the branch
/// issues, and what place of supply is decided against.
/// </summary>
/// <remarks>
/// Owned here rather than on Identity's branch record (<c>docs/architecture/module-ownership.md</c>
/// section 5.8) because it is a tax fact with its own dates: a branch re-registered under a new
/// number keeps the old one on the invoices already issued under it. At most one registration of a
/// branch is in force on any day, which the database enforces with an exclusion constraint as well
/// as the handler with a read.
/// </remarks>
public sealed class GstRegistration
{
    private GstRegistration()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private GstRegistration(Guid id, Guid organisationId, GstRegistrationDetails details, DateTimeOffset now, Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        CreatedAt = now;
        CreatedBy = by;
        Apply(details, now, by);
    }

    /// <summary>This registration.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The registration number.</summary>
    public string Gstin { get; private set; } = string.Empty;

    /// <summary>The state code.</summary>
    public string StateCode { get; private set; } = string.Empty;

    /// <summary>The registered legal name.</summary>
    public string LegalName { get; private set; } = string.Empty;

    /// <summary>The trade name, where it differs.</summary>
    public string? TradeName { get; private set; }

    /// <summary>The first day the registration applies to.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The last day, or null while it is open-ended.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>When the registration was recorded.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who recorded it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>The details as an administrator would re-enter them.</summary>
    public GstRegistrationDetails Details
        => new(BranchId, Gstin, StateCode, LegalName, TradeName, EffectiveFrom, EffectiveTo);

    /// <summary>Records a registration.</summary>
    /// <param name="id">Its identifier.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The registration, or the reason it was refused.</returns>
    public static Result<GstRegistration> Create(
        Guid id,
        Guid organisationId,
        GstRegistrationDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        var validated = details.Validate();

        return validated.IsFailure
            ? Result.Failure<GstRegistration>(validated.Error)
            : Result.Success(new GstRegistration(id, organisationId, details, now, by));
    }

    /// <summary>Replaces what is said about the registration. The branch never changes.</summary>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Amend(GstRegistrationDetails details, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return validated;
        }

        if (details.BranchId != BranchId)
        {
            return Result.Failure(BillingErrors.BranchImmutable);
        }

        Apply(details, now, by);

        return Result.Success();
    }

    /// <summary>Whether the registration applies on a day.</summary>
    /// <param name="day">The day, in the branch's calendar.</param>
    /// <returns>True when it does.</returns>
    public bool IsInForceOn(DateOnly day)
        => day >= EffectiveFrom && (EffectiveTo is null || day <= EffectiveTo);

    /// <summary>Whether two registrations would be in force on a common day.</summary>
    /// <param name="from">The other's first day.</param>
    /// <param name="to">The other's last day, or null while open-ended.</param>
    /// <returns>True when they overlap.</returns>
    public bool Overlaps(DateOnly from, DateOnly? to)
        => (EffectiveTo is null || from <= EffectiveTo) && (to is null || EffectiveFrom <= to);

    private void Apply(GstRegistrationDetails details, DateTimeOffset now, Guid? by)
    {
        BranchId = details.BranchId;
        Gstin = details.Gstin.ToUpperInvariant();
        StateCode = details.StateCode;
        LegalName = details.LegalName.Trim();
        TradeName = string.IsNullOrWhiteSpace(details.TradeName) ? null : details.TradeName.Trim();
        EffectiveFrom = details.EffectiveFrom;
        EffectiveTo = details.EffectiveTo;
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
