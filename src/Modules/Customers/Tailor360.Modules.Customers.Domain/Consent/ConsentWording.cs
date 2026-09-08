using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Consent;

/// <summary>
/// One published version of the words a customer was actually asked.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A published wording is immutable.</strong> Every consent record names the version it was
/// given against, and <c>docs/prd/configurable-vs-fixed.md</c> row 75 states the consequence: existing
/// records keep their wording version, and a new version does not silently re-consent anyone. Editing
/// the text of a version somebody has already agreed to would rewrite what they agreed to, which is
/// the one thing a consent record exists to prevent.
/// </para>
/// <para>
/// So there is no edit. Changing the wording publishes the next version, and the counter asks again
/// under it when the purpose next matters.
/// </para>
/// </remarks>
public sealed class ConsentWording
{
    /// <summary>The longest wording text the column holds.</summary>
    public const int MaximumTextLength = 4000;

    /// <summary>The first version number a purpose's wording is published under.</summary>
    public const int FirstVersion = 1;

    private ConsentWording()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private ConsentWording(
        Guid id,
        Guid purposeId,
        int version,
        string text,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        PurposeId = purposeId;
        Version = version;
        Text = text;
        PublishedAt = now;
        PublishedBy = by;
    }

    /// <summary>Identity of this wording version.</summary>
    public Guid Id { get; private set; }

    /// <summary>The purpose it words.</summary>
    public Guid PurposeId { get; private set; }

    /// <summary>The version number, counting from <see cref="FirstVersion"/> within the purpose.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// The words the customer was asked.
    /// </summary>
    /// <remarks>
    /// Never logged, never put in an audit summary and never in an operational export
    /// (<c>docs/nfr/data-classification.md</c> section 5.3). What is recorded about a decision is the
    /// purpose, the outcome and this <see cref="Version"/> — enough to prove what was agreed without
    /// copying the notice into the trail.
    /// </remarks>
    public string Text { get; private set; } = string.Empty;

    /// <summary>When this version was published.</summary>
    public DateTimeOffset PublishedAt { get; private set; }

    /// <summary>The Owner who published it, or null when it was seeded as reference data.</summary>
    public Guid? PublishedBy { get; private set; }

    /// <summary>Publishes a wording version.</summary>
    /// <param name="id">The identifier, from the generator.</param>
    /// <param name="purposeId">The purpose it words.</param>
    /// <param name="version">The version number, one more than the purpose's current highest.</param>
    /// <param name="text">The words. Required.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">The Owner publishing it, or null when seeded.</param>
    /// <returns>The version, or the reason it was refused.</returns>
    public static Result<ConsentWording> Publish(
        Guid id,
        Guid purposeId,
        int version,
        string? text,
        DateTimeOffset now,
        Guid? by = null)
    {
        var words = text?.Trim();

        if (string.IsNullOrEmpty(words))
        {
            return Result.Failure<ConsentWording>(CustomersErrors.Required("text"));
        }

        if (words.Length > MaximumTextLength)
        {
            return Result.Failure<ConsentWording>(CustomersErrors.TooLong("text", MaximumTextLength));
        }

        return version < FirstVersion
            ? Result.Failure<ConsentWording>(CustomersErrors.Required("version"))
            : Result.Success(new ConsentWording(id, purposeId, version, words, now, by));
    }
}
