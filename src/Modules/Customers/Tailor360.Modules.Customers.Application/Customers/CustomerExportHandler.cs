using Microsoft.Extensions.Options;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Options;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Customers.Application.Customers;

/// <summary>
/// Generates, serves and destroys the copy of a person's data that answers a subject-access request.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="CustomerHandler"/> because it is a different job with a different risk.
/// The other handler changes the record; this one copies it, and everything here is about keeping the
/// number of copies small and the time they exist short.
/// </para>
/// <para>
/// <strong>Three audit entries, not one.</strong> Generating is a state change and is audited by the
/// filter on the endpoint. Downloading is a <em>read</em>, and
/// <c>docs/nfr/data-classification.md</c> section 10 lists "an export download" among the reads that
/// are audited explicitly — so it is audited here, in the handler, because no filter watches a read.
/// Emptying is audited too: a copy of somebody's data ceasing to exist is worth being able to prove.
/// </para>
/// </remarks>
/// <param name="exports">The export store.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="options">How long an export lives.</param>
public sealed class CustomerExportHandler(
    IExportStore exports,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    IOptions<CustomerExportOptions> options)
{
    /// <summary>Recorded when an export is generated.</summary>
    public const string GeneratedAction = "customers.customer.exported";

    /// <summary>
    /// Recorded when the document is streamed to somebody.
    /// </summary>
    /// <remarks>
    /// Its own action rather than a second <see cref="GeneratedAction"/> entry, because "a copy was
    /// made" and "a copy was read" are different questions after the fact, and an investigator asking
    /// the second one should not have to tell them apart by their summaries.
    /// </remarks>
    public const string DownloadedAction = "customers.customer.export-downloaded";

    /// <summary>Recorded when the copy is destroyed.</summary>
    public const string PurgedAction = "customers.customer.export-purged";

    /// <summary>
    /// Generates an export of everything the module holds about one person.
    /// </summary>
    /// <remarks>
    /// Any export already live for that customer is emptied in the same transaction. One person's data
    /// is copied out of the record for one request at a time, so a member of staff clicking twice
    /// leaves one copy alive rather than two, each with its own lifetime running.
    /// </remarks>
    /// <param name="command">Who is exporting what, and why.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was generated, or the reason it was refused.</returns>
    public async Task<Result<CustomerExportReceipt>> GenerateAsync(
        GenerateCustomerExportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var subject = await exports.GatherAsync(
            command.CustomerId, command.OrganisationId, cancellationToken);

        if (subject is null)
        {
            return Result.Failure<CustomerExportReceipt>(CustomersErrors.CustomerNotFound);
        }

        var now = clock.UtcNow;
        var exportId = ids.NewId();
        var lifetime = options.Value.Lifetime;

        var document = CustomerExportDocument.Render(
            subject, now, now + lifetime, command.By, exportId, command.CustomerId);

        // The row belongs to the record the document is actually about. Keying it on the identifier
        // that was asked for would let one person's data live under two export rows — one per merged
        // identifier — and supersession would then never find the other, which is the one rule this
        // aggregate exists to keep.
        var generated = CustomerExport.Generate(
            exportId,
            command.OrganisationId,
            subject.Customer.Id,
            CustomerExportDocument.DocumentCode,
            CustomerExportDocument.DocumentVersion,
            CustomerExportDocument.Classification,
            document,
            CustomerExportDocument.ContentType,
            command.Reason,
            now,
            lifetime,
            command.By);

        if (generated.IsFailure)
        {
            return Result.Failure<CustomerExportReceipt>(generated.Error);
        }

        // Superseded before the new one is added, so the two changes commit together: there is never an
        // instant, even inside the transaction, where two live copies exist.
        var superseded = await exports.LiveForCustomerAsync(
            subject.Customer.Id, command.OrganisationId, cancellationToken);

        foreach (var earlier in superseded)
        {
            earlier.Purge(CustomerExportPurgeReason.Superseded, now);
        }

        exports.Add(generated.Value);

        var saved = await exports.TrySaveChangesAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<CustomerExportReceipt>(saved.Error);
        }

        // Save first, then record: the house order.
        await CustomerAudit.RecordAsync(
            audit,
            GeneratedAction,
            subject.Customer.Id,
            $"Generated a subject-access export of {generated.Value.ByteCount} bytes, classified "
            + $"{CustomerExportDocument.Classification}, downloadable until {generated.Value.ExpiresAt:u}. "
            + $"Earlier exports emptied: {superseded.Count}.",
            command.Reason,
            null,
            new CustomerExportAuditState(
                generated.Value.Id,
                generated.Value.DocumentCode,
                generated.Value.DocumentVersion,
                generated.Value.Classification,
                generated.Value.ByteCount,
                generated.Value.ExpiresAt,
                superseded.Count),
            cancellationToken);

        return Result.Success(new CustomerExportReceipt(
            generated.Value.Id,
            subject.Customer.Id,
            generated.Value.DocumentCode,
            generated.Value.DocumentVersion,
            generated.Value.Classification,
            generated.Value.ContentType,
            generated.Value.ByteCount,
            generated.Value.GeneratedAt,
            generated.Value.ExpiresAt,
            superseded.Count));
    }

    /// <summary>
    /// Streams a generated export, recording that it was read.
    /// </summary>
    /// <param name="query">Which export, for which customer, in which organisation.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The document and how to serve it, or the reason it was refused.</returns>
    public async Task<Result<CustomerExportContent>> DownloadAsync(
        ReadCustomerExportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var export = await exports.FindAsync(
            query.ExportId, query.CustomerId, query.OrganisationId, cancellationToken);

        if (export is null)
        {
            return Result.Failure<CustomerExportContent>(CustomersErrors.ExportNotFound);
        }

        var now = clock.UtcNow;

        if (!export.IsDownloadable(now))
        {
            return Result.Failure<CustomerExportContent>(CustomersErrors.ExportExpired);
        }

        var document = export.Document!;

        export.RecordDownload(now);

        var saved = await exports.TrySaveChangesAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<CustomerExportContent>(saved.Error);
        }

        // The read that section 10 requires to be audited explicitly. It is recorded against the
        // customer, not the export, because the question it answers later is "who has seen this
        // person's data", and that is asked of the person.
        await CustomerAudit.RecordAsync(
            audit,
            DownloadedAction,
            export.CustomerId,
            $"Downloaded the subject-access export generated at {export.GeneratedAt:u}. "
            + $"Downloads of this export so far: {export.DownloadCount}.",
            null,
            null,
            new CustomerExportAuditState(
                export.Id,
                export.DocumentCode,
                export.DocumentVersion,
                export.Classification,
                export.ByteCount,
                export.ExpiresAt,
                0),
            cancellationToken);

        return Result.Success(new CustomerExportContent(
            export.Id,
            document,
            export.ContentType,
            export.DocumentCode,
            export.Classification));
    }

    /// <summary>
    /// Empties every export whose lifetime has run out, and says how many went.
    /// </summary>
    /// <remarks>
    /// Called by the worker on a timer. Bounded by
    /// <see cref="CustomerExportOptions.PurgeBatchSize"/>, so a long outage leaves a backlog that
    /// drains over several passes rather than one pass that holds a transaction open over all of it.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many copies were destroyed.</returns>
    public async Task<Result<int>> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var expired = await exports.ExpiredHoldingDataAsync(
            now, options.Value.PurgeBatchSize, cancellationToken);

        if (expired.Count == 0)
        {
            return Result.Success(0);
        }

        foreach (var export in expired)
        {
            export.Purge(CustomerExportPurgeReason.Expired, now);
        }

        var saved = await exports.TrySaveChangesAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<int>(saved.Error);
        }

        // One entry per export rather than one for the batch. The trail is read by entity, and a
        // person asking what happened to their data should find the answer against their own record
        // rather than inside a summary of two hundred other people's.
        foreach (var export in expired)
        {
            await CustomerAudit.RecordAsync(
                audit,
                PurgedAction,
                export.CustomerId,
                $"Destroyed the copy of this record held by the subject-access export generated at "
                + $"{export.GeneratedAt:u}. It expired at {export.ExpiresAt:u} and had been downloaded "
                + $"{export.DownloadCount} time(s). The record that an export was taken is kept.",
                null,
                new CustomerExportAuditState(
                    export.Id,
                    export.DocumentCode,
                    export.DocumentVersion,
                    export.Classification,
                    export.ByteCount,
                    export.ExpiresAt,
                    0),
                null,
                cancellationToken);
        }

        return Result.Success(expired.Count);
    }
}

/// <summary>A request to export everything held about one person.</summary>
/// <param name="CustomerId">The person.</param>
/// <param name="OrganisationId">The caller's organisation. A record outside it is not found.</param>
/// <param name="Reason">Why the export was taken. Required, and kept.</param>
/// <param name="By">The actor.</param>
public sealed record GenerateCustomerExportCommand(
    Guid CustomerId,
    Guid OrganisationId,
    string? Reason,
    Guid? By);

/// <summary>A request to read a generated export.</summary>
/// <param name="ExportId">The export.</param>
/// <param name="CustomerId">The customer the route names. Checked against the export.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
public sealed record ReadCustomerExportQuery(Guid ExportId, Guid CustomerId, Guid OrganisationId);

/// <summary>What generating an export produced, without the copy itself.</summary>
/// <param name="ExportId">The export's identity, which is how it is downloaded.</param>
/// <param name="CustomerId">The person.</param>
/// <param name="DocumentCode">Which document it is.</param>
/// <param name="DocumentVersion">The document's shape version.</param>
/// <param name="Classification">The highest class it carries.</param>
/// <param name="ContentType">The media type the download is served as.</param>
/// <param name="ByteCount">How large it is.</param>
/// <param name="GeneratedAt">When it was made.</param>
/// <param name="ExpiresAt">When the download stops working.</param>
/// <param name="SupersededCount">How many earlier copies were destroyed to make it.</param>
public sealed record CustomerExportReceipt(
    Guid ExportId,
    Guid CustomerId,
    string DocumentCode,
    int DocumentVersion,
    string Classification,
    string ContentType,
    int ByteCount,
    DateTimeOffset GeneratedAt,
    DateTimeOffset ExpiresAt,
    int SupersededCount);

/// <summary>A generated export's bytes, and what to serve them as.</summary>
/// <param name="ExportId">The export's identity, which is also the download's filename.</param>
/// <param name="Document">The bytes.</param>
/// <param name="ContentType">The media type.</param>
/// <param name="DocumentCode">Which document it is.</param>
/// <param name="Classification">The highest class it carries.</param>
public sealed record CustomerExportContent(
    Guid ExportId,
    byte[] Document,
    string ContentType,
    string DocumentCode,
    string Classification);

/// <summary>
/// An export as the audit trail describes it: shape, never content.
/// </summary>
/// <param name="ExportId">Which export.</param>
/// <param name="DocumentCode">Which document.</param>
/// <param name="DocumentVersion">The document's shape version.</param>
/// <param name="Classification">The highest class it carried.</param>
/// <param name="ByteCount">How large it was. A size, not a value.</param>
/// <param name="ExpiresAt">When the download stopped or stops working.</param>
/// <param name="SupersededCount">How many earlier copies it destroyed, where that applies.</param>
internal sealed record CustomerExportAuditState(
    Guid ExportId,
    string DocumentCode,
    int DocumentVersion,
    string Classification,
    int ByteCount,
    DateTimeOffset ExpiresAt,
    int SupersededCount) : ICustomerAuditState;
