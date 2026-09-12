using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Invoicing;

/// <summary>The posted documents that are rendered.</summary>
public enum DocumentKind
{
    /// <summary>A tax invoice.</summary>
    Invoice = 0,

    /// <summary>A credit note.</summary>
    CreditNote = 1,

    /// <summary>A debit note.</summary>
    DebitNote = 2,
}

/// <summary>Where a rendering stands.</summary>
public enum DocumentArtifactStatus
{
    /// <summary>Requested at posting; the worker has not stored it yet.</summary>
    Pending = 0,

    /// <summary>Rendered, stored, and its checksum recorded.</summary>
    Completed = 1,

    /// <summary>Given up after the bounded attempts; an operational alert, never a reason to re-post (<c>INV-INV-08</c>).</summary>
    Failed = 2,
}

/// <summary>
/// One rendered document (#155): requested when the document is posted, completed by the worker that
/// renders it, stores the bytes under an opaque key in the <c>documents/</c> prefix and records the size
/// and the SHA-256 of what it stored. The key never carries the number or a name
/// (<c>docs/architecture/conventions.md</c> section 3.5), and no URL to the object exists: the bytes are
/// streamed by a route that re-authorises the caller.
/// </summary>
public sealed class DocumentArtifact
{
    /// <summary>How many times the worker tries before it gives up and raises the alert.</summary>
    public const int MaximumAttempts = 5;

    /// <summary>The media type every artefact is.</summary>
    public const string PdfContentType = "application/pdf";

    /// <summary>The longest error the row keeps: the code and the first line, never a stack.</summary>
    public const int MaximumErrorLength = 500;

    private DocumentArtifact()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private DocumentArtifact(Guid id, Guid organisationId, Guid branchId, DocumentKind kind, Guid documentId, string documentNumber, DateTimeOffset now)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        Kind = kind;
        DocumentId = documentId;
        DocumentNumber = documentNumber;
        Version = 1;
        Status = DocumentArtifactStatus.Pending;
        RequestedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch that issued the document.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>What kind of document.</summary>
    public DocumentKind Kind { get; private set; }

    /// <summary>The invoice or the note.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>The document's display number, for the download's file name.</summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>The rendering's version; one, until a re-render is ever asked for.</summary>
    public int Version { get; private set; }

    /// <summary>Where it stands.</summary>
    public DocumentArtifactStatus Status { get; private set; }

    /// <summary>The opaque object key, once stored.</summary>
    public string? ObjectKey { get; private set; }

    /// <summary>The media type, once stored.</summary>
    public string? ContentType { get; private set; }

    /// <summary>The size in bytes, once stored.</summary>
    public long? SizeBytes { get; private set; }

    /// <summary>The SHA-256 of the bytes stored, lower-case hex, once stored.</summary>
    public string? Sha256 { get; private set; }

    /// <summary>How many renderings have been attempted.</summary>
    public int Attempts { get; private set; }

    /// <summary>What the last attempt failed with, by code; null after success.</summary>
    public string? LastError { get; private set; }

    /// <summary>When the rendering was requested.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>When it was stored.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When the row last moved.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>True once the bytes are stored and may be streamed.</summary>
    public bool IsCompleted => Status == DocumentArtifactStatus.Completed;

    /// <summary>Requests a rendering.</summary>
    public static DocumentArtifact Request(Guid id, Guid organisationId, Guid branchId, DocumentKind kind, Guid documentId, string documentNumber, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        return new DocumentArtifact(id, organisationId, branchId, kind, documentId, documentNumber.Trim(), now);
    }

    /// <summary>Records what the worker stored.</summary>
    /// <param name="objectKey">The opaque key.</param>
    /// <param name="contentType">The media type.</param>
    /// <param name="sizeBytes">The size.</param>
    /// <param name="sha256">The checksum of the bytes stored, lower-case hex.</param>
    /// <param name="now">When.</param>
    public Result Complete(string objectKey, string contentType, long sizeBytes, string sha256, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        if (Status == DocumentArtifactStatus.Completed)
        {
            return Result.Failure(BillingErrors.DocumentAlreadyRendered);
        }

        if (sizeBytes <= 0 || sha256.Length != 64)
        {
            return Result.Failure(BillingErrors.Required("artefact"));
        }

        Status = DocumentArtifactStatus.Completed;
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        Attempts++;
        LastError = null;
        CompletedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Records a failed attempt; after the bounded number the artefact is failed for good.</summary>
    /// <param name="error">The code and the first line of what went wrong; never a stack, never content.</param>
    /// <param name="now">When.</param>
    /// <returns>True when the artefact has now failed for good.</returns>
    public bool RecordFailure(string error, DateTimeOffset now)
    {
        Attempts++;
        var trimmed = (error ?? string.Empty).Trim();
        LastError = trimmed.Length > MaximumErrorLength ? trimmed[..MaximumErrorLength] : trimmed;
        UpdatedAt = now;
        if (Attempts >= MaximumAttempts)
        {
            Status = DocumentArtifactStatus.Failed;
            return true;
        }

        return false;
    }
}
