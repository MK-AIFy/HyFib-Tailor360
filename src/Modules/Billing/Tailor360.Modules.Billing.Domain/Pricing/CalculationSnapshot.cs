using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>
/// One calculation as it was made: the request, the result and the configuration versions it used,
/// under the caller's reference. Append-only, because a historical figure must reproduce exactly
/// after every later publication (<c>INV-INV-03</c>, plan D10).
/// </summary>
/// <remarks>
/// The request and the result are held as the JSON the application layer wrote, opaque to this
/// entity: the domain fixes that they are kept and never changed, not their shape.
/// </remarks>
public sealed class CalculationSnapshot
{
    /// <summary>The longest reference a caller may key a calculation by.</summary>
    public const int MaximumReferenceLength = 200;

    /// <summary>
    /// The shape of the JSON this build writes. A later build that changes the shape reads an older
    /// snapshot by its version rather than by hoping the positional records still line up.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    private CalculationSnapshot()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CalculationSnapshot(
        Guid id,
        Guid organisationId,
        Guid branchId,
        string reference,
        Guid priceListVersionId,
        Guid taxConfigurationVersionId,
        Guid gstRegistrationId,
        string requestJson,
        string resultJson,
        DateTimeOffset calculatedAt,
        Guid? calculatedBy)
    {
        SchemaVersion = CurrentSchemaVersion;
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        Reference = reference;
        PriceListVersionId = priceListVersionId;
        TaxConfigurationVersionId = taxConfigurationVersionId;
        GstRegistrationId = gstRegistrationId;
        RequestJson = requestJson;
        ResultJson = resultJson;
        CalculatedAt = calculatedAt;
        CalculatedBy = calculatedBy;
    }

    /// <summary>The snapshot's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch priced for.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The caller's reference, unique within the organisation.</summary>
    public string Reference { get; private set; } = string.Empty;

    /// <summary>The price-list version used.</summary>
    public Guid PriceListVersionId { get; private set; }

    /// <summary>The tax configuration version used.</summary>
    public Guid TaxConfigurationVersionId { get; private set; }

    /// <summary>The registration the scheme was decided against.</summary>
    public Guid GstRegistrationId { get; private set; }

    /// <summary>Which shape of JSON the row holds.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>The request, as JSON.</summary>
    public string RequestJson { get; private set; } = string.Empty;

    /// <summary>The result, as JSON.</summary>
    public string ResultJson { get; private set; } = string.Empty;

    /// <summary>When it was calculated.</summary>
    public DateTimeOffset CalculatedAt { get; private set; }

    /// <summary>Who asked, or null for a background calculation.</summary>
    public Guid? CalculatedBy { get; private set; }

    /// <summary>Records a calculation.</summary>
    public static Result<CalculationSnapshot> Create(
        Guid id,
        Guid organisationId,
        Guid branchId,
        string reference,
        Guid priceListVersionId,
        Guid taxConfigurationVersionId,
        Guid gstRegistrationId,
        string requestJson,
        string resultJson,
        DateTimeOffset calculatedAt,
        Guid? calculatedBy)
    {
        var checkedReference = CheckReference(reference);
        if (checkedReference.IsFailure)
        {
            return Result.Failure<CalculationSnapshot>(checkedReference.Error);
        }

        if (string.IsNullOrWhiteSpace(requestJson) || string.IsNullOrWhiteSpace(resultJson))
        {
            return Result.Failure<CalculationSnapshot>(BillingErrors.Required("result"));
        }

        return Result.Success(new CalculationSnapshot(
            id, organisationId, branchId, reference.Trim(), priceListVersionId, taxConfigurationVersionId,
            gstRegistrationId, requestJson, resultJson, calculatedAt, calculatedBy));
    }

    /// <summary>Checks a reference: present, and short enough to index.</summary>
    public static Result CheckReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Result.Failure(BillingErrors.Required("reference"));
        }

        return reference.Trim().Length > MaximumReferenceLength
            ? Result.Failure(BillingErrors.TooLong("reference", MaximumReferenceLength))
            : Result.Success();
    }
}
