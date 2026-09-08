using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Preferences;

/// <summary>
/// How a customer wants to be reached, if at all.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A preference is not a consent, and the two are checked separately.</strong> Consent says
/// whether the shop may send about a purpose at all; this says how, in which language, and when not to.
/// Notifications (#47) evaluates both before every send, and a refusal on either is recorded as a
/// suppression with its reason (<c>docs/prd/glossary.md</c>). Allowing a channel here does not consent
/// to anything, and consenting does not oblige the shop to use a channel the customer switched off.
/// </para>
/// <para>
/// <strong>Unlike a consent record, a preference is editable.</strong> It is a current instruction
/// rather than evidence of something said at a moment, so it is updated in place and each change
/// publishes <c>customers.preferences-changed.v1</c>. What was preferred last year is not needed to
/// prove anything, and the audit trail carries the change.
/// </para>
/// <para>
/// <strong>An empty channel set is a valid, meaningful answer.</strong> It is how a customer says "do
/// not message me" without withdrawing consent to the purposes themselves — she may still want her
/// measurements kept and still be told things at the counter. Nothing here treats it as unset.
/// </para>
/// </remarks>
public sealed class CommunicationPreferences
{
    private readonly List<CommunicationChannel> _allowedChannels = [];

    private CommunicationPreferences()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CommunicationPreferences(
        Guid customerId,
        Guid organisationId,
        IEnumerable<CommunicationChannel> allowedChannels,
        string language,
        QuietHours? quietHours,
        DateTimeOffset now,
        Guid? by)
    {
        CustomerId = customerId;
        OrganisationId = organisationId;
        _allowedChannels.AddRange(allowedChannels);
        Language = language;
        QuietHours = quietHours;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>The customer. One row per customer, so this is the key.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The organisation. There is exactly one (BR-1).</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The channels the customer will accept, in a stable order. May be empty.</summary>
    public IReadOnlyList<CommunicationChannel> AllowedChannels => _allowedChannels;

    /// <summary>The language the customer is written to in.</summary>
    public string Language { get; private set; } = CustomerDetails.DefaultLanguage;

    /// <summary>The window the customer would rather not hear from the shop in, where they named one.</summary>
    public QuietHours? QuietHours { get; private set; }

    /// <summary>When the preference was first recorded.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who first recorded it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Records a customer's preference for the first time.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="allowedChannels">The channels they accept. An empty set is valid.</param>
    /// <param name="language">The language to write to them in, or null for the default.</param>
    /// <param name="quietHours">The window they would rather not be messaged in, or null.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">The actor.</param>
    /// <returns>The preference, or the reason it was refused.</returns>
    public static Result<CommunicationPreferences> Record(
        Guid customerId,
        Guid organisationId,
        IEnumerable<CommunicationChannel>? allowedChannels,
        string? language,
        QuietHours? quietHours,
        DateTimeOffset now,
        Guid? by)
    {
        var channels = ReadChannels(allowedChannels);

        if (channels.IsFailure)
        {
            return Result.Failure<CommunicationPreferences>(channels.Error);
        }

        var tag = ReadLanguage(language);

        return tag.IsFailure
            ? Result.Failure<CommunicationPreferences>(tag.Error)
            : Result.Success(new CommunicationPreferences(
                customerId, organisationId, channels.Value, tag.Value, quietHours, now, by));
    }

    /// <summary>Replaces the whole preference with what the customer now says.</summary>
    /// <remarks>
    /// The whole set, not a list of additions, so that the audit entry reads as a state rather than as
    /// a difference — the same reason the role and branch assignments of #25 are replaced rather than
    /// patched. "Which channels does she accept" has one answer, and a reader of the trail should see
    /// it without replaying every change since.
    /// </remarks>
    /// <param name="allowedChannels">The channels they accept now. An empty set is valid.</param>
    /// <param name="language">The language, or null for the default.</param>
    /// <param name="quietHours">The window, or null for none.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Replace(
        IEnumerable<CommunicationChannel>? allowedChannels,
        string? language,
        QuietHours? quietHours,
        DateTimeOffset now,
        Guid? by)
    {
        var channels = ReadChannels(allowedChannels);

        if (channels.IsFailure)
        {
            return Result.Failure(channels.Error);
        }

        var tag = ReadLanguage(language);

        if (tag.IsFailure)
        {
            return Result.Failure(tag.Error);
        }

        _allowedChannels.Clear();
        _allowedChannels.AddRange(channels.Value);
        Language = tag.Value;
        QuietHours = quietHours;
        UpdatedAt = now;
        UpdatedBy = by;

        return Result.Success();
    }

    /// <summary>Whether a channel may carry a message to this customer.</summary>
    /// <param name="channel">The channel Notifications is considering.</param>
    /// <returns>True when the customer accepts it.</returns>
    public bool Allows(CommunicationChannel channel) => _allowedChannels.Contains(channel);

    /// <summary>
    /// Reads the channel set: duplicates collapsed, order made stable, unknown members refused.
    /// </summary>
    /// <remarks>
    /// The order is fixed by the enumeration rather than by the order somebody sent them, so that two
    /// requests saying the same thing produce the same row and the same audit entry. An undefined
    /// member is refused for the reason an undefined consent decision is: it arrives from a cast, and
    /// a channel nothing can send on is not a preference.
    /// </remarks>
    private static Result<List<CommunicationChannel>> ReadChannels(
        IEnumerable<CommunicationChannel>? allowedChannels)
    {
        var channels = allowedChannels?.ToList() ?? [];

        foreach (var channel in channels.Where(channel => !Enum.IsDefined(channel)))
        {
            return Result.Failure<List<CommunicationChannel>>(
                CustomersErrors.CommunicationChannelNotUnderstood("allowedChannels"));
        }

        return Result.Success(channels.Distinct().Order().ToList());
    }

    private static Result<string> ReadLanguage(string? language)
    {
        var tag = language?.Trim();

        if (string.IsNullOrEmpty(tag))
        {
            return Result.Success(CustomerDetails.DefaultLanguage);
        }

        return CustomerDetails.SupportedLanguages.Contains(tag, StringComparer.OrdinalIgnoreCase)
            ? Result.Success(tag)
            : Result.Failure<string>(CustomersErrors.LanguageNotSupported("language"));
    }
}
