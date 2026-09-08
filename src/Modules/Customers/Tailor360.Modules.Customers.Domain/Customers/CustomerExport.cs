using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Customers;

/// <summary>
/// One generated answer to a subject-access request: a copy of what the shop holds about one person,
/// held briefly so it can be downloaded, then emptied.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The row outlives the copy.</strong> Two different things are recorded here and they have
/// two different lifetimes. That an export was generated, by whom, for whom and why is evidence of how
/// the shop answered a request, and it is kept. The <em>document</em> is a second copy of somebody's
/// personal data sitting outside the record it came from, and it is destroyed as soon as it has served
/// its purpose. So <see cref="Purge"/> empties <see cref="Document"/> and leaves everything else,
/// which is the same shape <c>customer_merges</c> uses for its reason
/// (<c>docs/nfr/data-classification.md</c> section 5.2.1): keep the evidence, drop the content.
/// </para>
/// <para>
/// <strong>Only one live copy of a person's data at a time.</strong> Generating an export supersedes
/// the previous one for that customer, which empties it. Without that rule a member of staff clicking
/// twice would leave two copies alive, each with its own week to run, and the number of copies would
/// only ever grow.
/// </para>
/// <para>
/// <strong>Why the document is held in this schema rather than object storage.</strong>
/// <c>docs/nfr/data-classification.md</c> section 9 says export artefacts live under the
/// <c>exports/</c> prefix. That prefix belongs to Reporting
/// (<c>docs/architecture/module-ownership.md</c>), which is not built until #44, and no object-storage
/// abstraction exists in the solution yet — so there is no prefix to write to and no client to write
/// with. A subject-access export is one person's record and measures in kilobytes, so it is held in
/// the module's own schema until the export service exists, and the endpoint streams it. What section
/// 9 is actually protecting — audited, expiring, streamed by a re-authorising endpoint and never
/// linked — is satisfied either way, and that is the part this type enforces.
/// </para>
/// </remarks>
public sealed class CustomerExport
{
    /// <summary>
    /// The longest an export may live, whatever configuration asks for.
    /// </summary>
    /// <remarks>
    /// A bound rather than a retention period, and the distinction matters because
    /// <c>docs/prd/assumptions-and-open-decisions.md</c> <b>OD-08</b> — how long the business keeps
    /// each class of data — is an open decision nobody here may answer. This is not that. It is a
    /// security ceiling on a transient second copy of personal data, in the same spirit as
    /// <c>RecoveryToken.MaximumLifetime</c>: whatever period the business eventually sets for its
    /// records, a generated copy of them sitting in a download table has no business outliving the
    /// request it was made for. The operational value is configuration, defaulting to the <b>proposed</b>
    /// seven days in <c>docs/nfr/data-classification.md</c> section 9.
    /// </remarks>
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(30);

    /// <summary>The shortest lifetime that is usable at all.</summary>
    /// <remarks>
    /// Long enough that a member of staff who generates an export, is interrupted by a customer at the
    /// counter and comes back to it still has a working download.
    /// </remarks>
    public static readonly TimeSpan MinimumLifetime = TimeSpan.FromMinutes(15);

    /// <summary>The longest reason the column holds.</summary>
    /// <remarks>The bound the other reasoned commands on this aggregate use.</remarks>
    public const int MaximumReasonLength = 500;

    private CustomerExport()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CustomerExport(
        Guid id,
        Guid organisationId,
        Guid customerId,
        string documentCode,
        int documentVersion,
        string classification,
        byte[] document,
        string contentType,
        string? reason,
        DateTimeOffset generatedAt,
        DateTimeOffset expiresAt,
        Guid? generatedBy)
    {
        Id = id;
        OrganisationId = organisationId;
        CustomerId = customerId;
        DocumentCode = documentCode;
        DocumentVersion = documentVersion;
        Classification = classification;
        Document = document;
        ByteCount = document.Length;
        ContentType = contentType;
        Reason = reason;
        GeneratedAt = generatedAt;
        ExpiresAt = expiresAt;
        GeneratedBy = generatedBy;
    }

    /// <summary>Identity of the export. A UUIDv7, and the only thing that names it anywhere.</summary>
    /// <remarks>
    /// It is also the download filename, which is why it matters that it carries nothing about the
    /// person: personal data is never an identifier, a filename or a URL segment (CLAUDE.md rule 8).
    /// </remarks>
    public Guid Id { get; private set; }

    /// <summary>The organisation the export was taken in.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The customer the export is about.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Which document this is, so a file found later can be identified.</summary>
    public string DocumentCode { get; private set; } = string.Empty;

    /// <summary>The document's shape version, so a reader knows what to expect of it.</summary>
    public int DocumentVersion { get; private set; }

    /// <summary>
    /// The highest data class the document carries, which section 9 requires an export to be marked
    /// with. Stored as well as written into the document, so the row can be reasoned about without
    /// opening the copy.
    /// </summary>
    public string Classification { get; private set; } = string.Empty;

    /// <summary>The rendered document, or null once it has been emptied.</summary>
    public byte[]? Document { get; private set; }

    /// <summary>How large the document was, kept after it is emptied.</summary>
    public int ByteCount { get; private set; }

    /// <summary>The media type the download is served as.</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>
    /// Why the export was taken. Nullable for the same single reason <c>customer_merges.reason</c> is:
    /// so #57 can redact free text a member of staff typed about a named person without destroying the
    /// evidence that the export happened.
    /// </summary>
    public string? Reason { get; private set; }

    /// <summary>When it was generated, from <c>IClock</c>.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>When the download stops working.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Who generated it.</summary>
    public Guid? GeneratedBy { get; private set; }

    /// <summary>How many times it has been downloaded.</summary>
    public int DownloadCount { get; private set; }

    /// <summary>When it was last downloaded, if it ever was.</summary>
    public DateTimeOffset? LastDownloadedAt { get; private set; }

    /// <summary>When the document was emptied, if it has been.</summary>
    public DateTimeOffset? PurgedAt { get; private set; }

    /// <summary>Why it was emptied, if it has been.</summary>
    public CustomerExportPurgeReason? PurgeReason { get; private set; }

    /// <summary>True while the document is still there to be downloaded.</summary>
    /// <param name="now">The current instant.</param>
    /// <returns>Whether a download would succeed.</returns>
    public bool IsDownloadable(DateTimeOffset now)
        => Document is not null && PurgedAt is null && now < ExpiresAt;

    /// <summary>Generates an export.</summary>
    /// <param name="id">The identity, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="customerId">The customer the document is about.</param>
    /// <param name="documentCode">Which document this is.</param>
    /// <param name="documentVersion">The document's shape version.</param>
    /// <param name="classification">The highest class the document carries.</param>
    /// <param name="document">The rendered bytes.</param>
    /// <param name="contentType">The media type.</param>
    /// <param name="reason">Why the export was taken. Required.</param>
    /// <param name="generatedAt">The instant, from <c>IClock</c>.</param>
    /// <param name="lifetime">How long the download works for.</param>
    /// <param name="generatedBy">The actor.</param>
    /// <returns>The export, or the reason it was refused.</returns>
    public static Result<CustomerExport> Generate(
        Guid id,
        Guid organisationId,
        Guid customerId,
        string documentCode,
        int documentVersion,
        string classification,
        byte[] document,
        string contentType,
        string? reason,
        DateTimeOffset generatedAt,
        TimeSpan lifetime,
        Guid? generatedBy)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<CustomerExport>(CustomersErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<CustomerExport>(CustomersErrors.Required("organisationId"));
        }

        if (customerId == Guid.Empty)
        {
            return Result.Failure<CustomerExport>(CustomersErrors.Required("customerId"));
        }

        if (document is null || document.Length == 0)
        {
            return Result.Failure<CustomerExport>(CustomersErrors.Required("document"));
        }

        if (string.IsNullOrWhiteSpace(classification))
        {
            return Result.Failure<CustomerExport>(CustomersErrors.Required("classification"));
        }

        var trimmed = reason?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<CustomerExport>(CustomersErrors.ReasonRequired);
        }

        if (trimmed.Length > MaximumReasonLength)
        {
            return Result.Failure<CustomerExport>(
                CustomersErrors.TooLong("reason", MaximumReasonLength));
        }

        // Clamped rather than refused. The lifetime is configuration, not caller input, and a
        // deployment that has set it badly should still produce a working export with a sane ceiling
        // instead of failing every subject-access request until somebody notices.
        var bounded = lifetime < MinimumLifetime
            ? MinimumLifetime
            : lifetime > MaximumLifetime ? MaximumLifetime : lifetime;

        return Result.Success(new CustomerExport(
            id,
            organisationId,
            customerId,
            documentCode,
            documentVersion,
            classification,
            document,
            contentType,
            trimmed,
            generatedAt,
            generatedAt + bounded,
            generatedBy));
    }

    /// <summary>Records that the document was streamed to somebody.</summary>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    public void RecordDownload(DateTimeOffset now)
    {
        DownloadCount++;
        LastDownloadedAt = now;
    }

    /// <summary>
    /// Empties the document, keeping the evidence that the export happened.
    /// </summary>
    /// <remarks>
    /// Idempotent: purging an already-purged export changes nothing and keeps the first reason, so a
    /// cleanup job that runs twice over the same row does not rewrite why it went.
    /// </remarks>
    /// <param name="reason">Why the copy went.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    public void Purge(CustomerExportPurgeReason reason, DateTimeOffset now)
    {
        if (PurgedAt is not null)
        {
            return;
        }

        Document = null;
        PurgedAt = now;
        PurgeReason = reason;
    }
}

/// <summary>Why a generated export stopped being downloadable.</summary>
public enum CustomerExportPurgeReason
{
    /// <summary>Its lifetime ran out and the cleanup job emptied it.</summary>
    Expired = 1,

    /// <summary>A newer export for the same customer replaced it.</summary>
    Superseded = 2,
}
