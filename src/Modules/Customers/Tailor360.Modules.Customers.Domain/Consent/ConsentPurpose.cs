using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Consent;

/// <summary>
/// One thing a customer is asked to agree to, and the versions of the words used to ask.
/// </summary>
/// <remarks>
/// <para>
/// A purpose is <strong>configuration, not code</strong>
/// (<c>docs/prd/configurable-vs-fixed.md</c> row 75): an Owner maintains the set, and the wording of
/// each is reviewed before publication. <see cref="ConsentPurposeKeys"/> holds the five the platform's
/// own rules name and <c>init-reference-data</c> seeds; a shop may hold more.
/// </para>
/// <para>
/// <strong>A purpose is retired, never deleted.</strong> Consent records name it for as long as they
/// exist, and they exist to be evidence. Retiring stops it being asked about; it does not remove what
/// anybody already said.
/// </para>
/// </remarks>
public sealed class ConsentPurpose
{
    /// <summary>The longest display name the column holds.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest description the column holds.</summary>
    public const int MaximumDescriptionLength = 500;

    private readonly List<ConsentWording> _wordings = [];

    private ConsentPurpose()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private ConsentPurpose(
        Guid id,
        Guid organisationId,
        string key,
        string name,
        string? description,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        Key = key;
        Name = name;
        Description = description;
        IsRetired = false;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>Identity of the purpose.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation. There is exactly one (BR-1).</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>
    /// The stable key other modules and consent records name it by.
    /// </summary>
    /// <remarks>Fixed at creation. Renaming it would orphan every record that names it.</remarks>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The name a counter screen shows.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What agreeing to it allows, in a sentence a person at a counter can read out.</summary>
    public string? Description { get; private set; }

    /// <summary>True once the purpose is no longer asked about.</summary>
    public bool IsRetired { get; private set; }

    /// <summary>The published wording versions, oldest first.</summary>
    public IReadOnlyCollection<ConsentWording> Wordings => _wordings;

    /// <summary>The highest version published, or zero when none has been.</summary>
    public int CurrentWordingVersion => _wordings.Count == 0 ? 0 : _wordings.Max(wording => wording.Version);

    /// <summary>When the purpose was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it, or null when it was seeded.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Defines a purpose, without any wording yet.</summary>
    /// <param name="id">The identifier, from the generator.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="key">The stable key. Lower case, digits and underscores.</param>
    /// <param name="name">The name a screen shows.</param>
    /// <param name="description">What agreeing allows.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">The actor, or null when seeded.</param>
    /// <returns>The purpose, or the reason it was refused.</returns>
    public static Result<ConsentPurpose> Define(
        Guid id,
        Guid organisationId,
        string? key,
        string? name,
        string? description,
        DateTimeOffset now,
        Guid? by = null)
    {
        var purposeKey = key?.Trim();

        if (string.IsNullOrEmpty(purposeKey))
        {
            return Result.Failure<ConsentPurpose>(CustomersErrors.Required("key"));
        }

        if (purposeKey.Length > ConsentPurposeKeys.MaximumLength)
        {
            return Result.Failure<ConsentPurpose>(
                CustomersErrors.TooLong("key", ConsentPurposeKeys.MaximumLength));
        }

        // The key is written into every consent record and read by other modules, so it is held to a
        // shape that survives a URL, a log line and a configuration file unaltered.
        if (!purposeKey.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_'))
        {
            return Result.Failure<ConsentPurpose>(CustomersErrors.ConsentPurposeKeyNotAllowed("key"));
        }

        var displayName = name?.Trim();

        if (string.IsNullOrEmpty(displayName))
        {
            return Result.Failure<ConsentPurpose>(CustomersErrors.Required("name"));
        }

        if (displayName.Length > MaximumNameLength)
        {
            return Result.Failure<ConsentPurpose>(CustomersErrors.TooLong("name", MaximumNameLength));
        }

        var text = description?.Trim();

        return text is { Length: > MaximumDescriptionLength }
            ? Result.Failure<ConsentPurpose>(
                CustomersErrors.TooLong("description", MaximumDescriptionLength))
            : Result.Success(new ConsentPurpose(
                id,
                organisationId,
                purposeKey,
                displayName,
                string.IsNullOrEmpty(text) ? null : text,
                now,
                by));
    }

    /// <summary>Publishes the next wording version.</summary>
    /// <param name="id">The identifier, from the generator.</param>
    /// <param name="text">The words the customer will be asked.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">The Owner publishing it, or null when seeded.</param>
    /// <returns>The version published, or the reason it was refused.</returns>
    public Result<ConsentWording> PublishWording(Guid id, string? text, DateTimeOffset now, Guid? by = null)
    {
        if (IsRetired)
        {
            return Result.Failure<ConsentWording>(CustomersErrors.ConsentPurposeRetired(Key));
        }

        var published = ConsentWording.Publish(id, Id, CurrentWordingVersion + 1, text, now, by);

        if (published.IsFailure)
        {
            return published;
        }

        _wordings.Add(published.Value);
        UpdatedAt = now;
        UpdatedBy = by;

        return published;
    }

    /// <summary>Stops the purpose being asked about, without touching what anybody already said.</summary>
    /// <param name="now">The clock.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Retire(DateTimeOffset now, Guid? by)
    {
        if (IsRetired)
        {
            return Result.Failure(CustomersErrors.ConsentPurposeRetired(Key));
        }

        IsRetired = true;
        UpdatedAt = now;
        UpdatedBy = by;

        return Result.Success();
    }
}
