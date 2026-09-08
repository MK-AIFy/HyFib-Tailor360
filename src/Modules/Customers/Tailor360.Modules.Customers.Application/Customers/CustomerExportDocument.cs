using System.Text.Json;
using System.Text.Json.Serialization;
using Tailor360.Modules.Customers.Application.Abstractions;

namespace Tailor360.Modules.Customers.Application.Customers;

/// <summary>
/// Renders what the shop holds about one person into the document a subject-access request is
/// answered with.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the one place that decides what leaves.</strong> Everything else in the slice
/// decides <em>whether</em> an export may be taken; this decides what is in it. So the shape is
/// written out field by field rather than serialised from the aggregate: a property added to
/// <c>Customer</c> next year must not silently appear in an export of somebody's data because a
/// serialiser found it.
/// </para>
/// <para>
/// <strong>JSON, not a spreadsheet.</strong> <c>docs/nfr/data-classification.md</c> section 9 requires
/// exports to be safe by construction, and its rule is about cells: a value beginning <c>=</c>,
/// <c>+</c>, <c>-</c>, <c>@</c>, tab or carriage return is a formula when a spreadsheet opens it. JSON
/// has no cells and no formulae, so the rule is satisfied by having no such surface rather than by
/// escaping one. If a tabular rendering is ever added it inherits the rule, and the injection corpus
/// test that covers every export path arrives with #46.
/// </para>
/// <para>
/// <strong>What is deliberately absent.</strong> No images — the plan says so and section 5.5
/// classifies them separately. No duplicate-candidate scores and no merge reasons: section 5.2.1 says
/// neither ever leaves Customers, and a score asserting that this person was once thought to be
/// somebody else is the shop's working, not the subject's record. Measurements are absent because the
/// system does not hold any yet; see <see cref="Measurements"/>.
/// </para>
/// </remarks>
public static class CustomerExportDocument
{
    /// <summary>Which document this is. Written into the file and stored on the row.</summary>
    public const string DocumentCode = "customers.subject-access-export";

    /// <summary>
    /// The document's shape version. Raised when a reader would have to be changed — a field removed
    /// or given a new meaning. Adding a section does not raise it.
    /// </summary>
    public const int DocumentVersion = 1;

    /// <summary>
    /// The highest class the document carries, which section 9 requires an export to be marked with.
    /// </summary>
    /// <remarks>
    /// <b>Personal</b> today, because the document carries identity, contact details, consent and
    /// preferences — every one of them Personal under sections 5.2 and 5.3 — and carries no
    /// measurements, which are the Sensitive Personal part. When measurements join the document with
    /// #28 this becomes <c>Sensitive Personal</c>, and the marking is a stored column precisely so
    /// that a document generated before that change still says what it actually held.
    /// </remarks>
    public const string Classification = "Personal";

    /// <summary>The media type the download is served as.</summary>
    public const string ContentType = "application/json; charset=utf-8";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Renders the document.</summary>
    /// <param name="subject">What the module holds about the person.</param>
    /// <param name="generatedAt">The instant, from <c>IClock</c>.</param>
    /// <param name="expiresAt">When the download stops working.</param>
    /// <param name="generatedBy">The actor, as an identifier.</param>
    /// <param name="exportId">The export's identity.</param>
    /// <param name="requestedCustomerId">
    /// The identifier the request named, which is not always the record the document describes: a
    /// record folded in by a merge still answers to its old number, and the export follows the pointer
    /// to the surviving record. Recording both is what stops the document reading as an answer about a
    /// different person.
    /// </param>
    /// <returns>The rendered bytes, UTF-8.</returns>
    public static byte[] Render(
        CustomerSubjectData subject,
        DateTimeOffset generatedAt,
        DateTimeOffset expiresAt,
        Guid? generatedBy,
        Guid exportId,
        Guid requestedCustomerId)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var customer = subject.Customer;

        var document = new ExportBody(
            new ExportHeader(
                DocumentCode,
                DocumentVersion,
                Classification,
                exportId,
                generatedAt,
                expiresAt,
                generatedBy,
                "organisation",
                requestedCustomerId,
                requestedCustomerId != customer.Id),
            new ExportProfile(
                customer.Id,
                customer.CustomerNumber,
                customer.DisplayName,
                customer.NativeName,
                customer.PhoneE164,
                customer.AlternatePhoneE164,
                customer.Email,
                customer.AddressLine,
                customer.Locality,
                customer.Postcode,
                customer.Language,
                customer.Status.ToString(),
                customer.CreatedAt,
                customer.UpdatedAt,
                customer.DeactivatedAt,
                customer.MergedIntoCustomerId,
                customer.MergedAt,
                [.. customer.Aliases
                    .OrderBy(alias => alias.RecordedAt)
                    .Select(alias => new ExportAlias(alias.Kind.ToString(), alias.Value, alias.RecordedAt))]),
            [.. subject.Consent.Select(record => new ExportConsent(
                record.PurposeKey,
                record.Decision.ToString(),
                record.WordingVersion,
                record.Source,
                record.RecordedAt))],
            subject.Preferences is null
                ? null
                : new ExportPreferences(
                    [.. subject.Preferences.AllowedChannels.Select(channel => channel.ToString())],
                    subject.Preferences.Language,
                    subject.Preferences.QuietHours?.Start.ToString("HH:mm", null),
                    subject.Preferences.QuietHours?.End.ToString("HH:mm", null),
                    subject.Preferences.UpdatedAt),
            Measurements);

        return JsonSerializer.SerializeToUtf8Bytes(document, Options);
    }

    /// <summary>
    /// The measurement section, which is honest about holding nothing.
    /// </summary>
    /// <remarks>
    /// The plan's export includes a measurement summary, and section 5.4 says measurements belong in a
    /// data-subject export. There are none to include: the module has no measurement tables until #28
    /// builds them. So the section says the system holds no measurements <em>at all</em> rather than
    /// reporting an empty list, because an empty list would read as "we looked and this person has
    /// none", which is a different and untrue statement to make in answer to a subject-access request.
    /// </remarks>
    private static ExportMeasurements Measurements { get; } = new(
        false,
        "This system does not yet record measurements, so none are held for anybody. When measurement "
        + "records are introduced they will appear here.",
        []);

    private sealed record ExportBody(
        [property: JsonPropertyName("document")] ExportHeader Document,
        [property: JsonPropertyName("profile")] ExportProfile Profile,
        [property: JsonPropertyName("consentHistory")] IReadOnlyList<ExportConsent> ConsentHistory,
        [property: JsonPropertyName("communicationPreferences")] ExportPreferences? CommunicationPreferences,
        [property: JsonPropertyName("measurements")] ExportMeasurements Measurements);

    private sealed record ExportHeader(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("classification")] string Classification,
        [property: JsonPropertyName("exportId")] Guid ExportId,
        [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
        [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
        [property: JsonPropertyName("generatedByUserId")] Guid? GeneratedByUserId,
        [property: JsonPropertyName("branchScope")] string BranchScope,
        [property: JsonPropertyName("requestedCustomerId")] Guid RequestedCustomerId,
        [property: JsonPropertyName("followedAMerge")] bool FollowedAMerge);

    private sealed record ExportProfile(
        [property: JsonPropertyName("customerId")] Guid CustomerId,
        [property: JsonPropertyName("customerNumber")] string CustomerNumber,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("nativeName")] string? NativeName,
        [property: JsonPropertyName("phone")] string Phone,
        [property: JsonPropertyName("alternatePhone")] string? AlternatePhone,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("addressLine")] string? AddressLine,
        [property: JsonPropertyName("locality")] string? Locality,
        [property: JsonPropertyName("postcode")] string? Postcode,
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("deactivatedAt")] DateTimeOffset? DeactivatedAt,
        [property: JsonPropertyName("mergedIntoCustomerId")] Guid? MergedIntoCustomerId,
        [property: JsonPropertyName("mergedAt")] DateTimeOffset? MergedAt,
        [property: JsonPropertyName("aliases")] IReadOnlyList<ExportAlias> Aliases);

    private sealed record ExportAlias(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("recordedAt")] DateTimeOffset RecordedAt);

    private sealed record ExportConsent(
        [property: JsonPropertyName("purpose")] string Purpose,
        [property: JsonPropertyName("decision")] string Decision,
        [property: JsonPropertyName("wordingVersion")] int WordingVersion,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("recordedAt")] DateTimeOffset RecordedAt);

    private sealed record ExportPreferences(
        [property: JsonPropertyName("allowedChannels")] IReadOnlyList<string> AllowedChannels,
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("quietHoursStart")] string? QuietHoursStart,
        [property: JsonPropertyName("quietHoursEnd")] string? QuietHoursEnd,
        [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

    private sealed record ExportMeasurements(
        [property: JsonPropertyName("held")] bool Held,
        [property: JsonPropertyName("note")] string Note,
        [property: JsonPropertyName("records")] IReadOnlyList<string> Records);
}
