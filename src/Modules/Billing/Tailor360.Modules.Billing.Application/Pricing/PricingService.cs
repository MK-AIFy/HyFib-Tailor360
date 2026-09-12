using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Application.Pricing;

/// <summary>
/// <see cref="IPricingService"/>: finds the configuration in force, hands it to the engine, and keeps
/// what the engine produced under the caller's reference.
/// </summary>
/// <remarks>
/// <para>
/// The caller's <c>billing.override_price</c> is read from the session, not from the request: a request
/// cannot claim the permission, and the worker's principal carries what its job declared. The catalogue
/// marks that permission <c>RequiresMfa</c> and <c>RequiresStepUp</c>, and those hold here as they would
/// on an endpoint declaring it: a session that has not recently re-authenticated may not override,
/// whatever it holds. An override or a discount that needed the permission is audited with its
/// variance when the calculation is stored; a calculation without a reference is priced, answered and
/// forgotten, so there is nothing to audit.
/// </para>
/// <para>
/// A reference is answered from its snapshot only for the request that made it: the same reference with
/// a different body is a conflict, not a stale figure returned as if it were current. Two calculations
/// of one thing carry two references.
/// </para>
/// <para>
/// A stored snapshot answers every later request under its reference without recomputing, which is
/// what makes a figure reproducible after the versions it used are retired. <see cref="PreviewAsync"/>
/// prices against a named version — a draft, so that an administrator sees what a publication would
/// do — and stores nothing.
/// </para>
/// </remarks>
public sealed class PricingService(
    IPriceListStore priceLists,
    ITaxConfigurationStore taxConfigurations,
    IGstRegistrationStore registrations,
    ICalculationSnapshotStore snapshots,
    ICurrentUser caller,
    IOptions<StepUpOptions> stepUp,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
    : IPricingService
{
    /// <summary>A calculation used the caller's permission to go beyond the catalogue or a rule's threshold.</summary>
    public const string OverriddenAction = "billing.price.overridden";

    /// <inheritdoc />
    public async Task<Result<PricingResult>> PriceAsync(PricingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Reference is { } reference)
        {
            var referenced = CalculationSnapshot.CheckReference(reference);
            if (referenced.IsFailure)
            {
                return Result.Failure<PricingResult>(referenced.Error);
            }

            if (await snapshots.FindAsync(request.OrganisationId, reference.Trim(), cancellationToken) is { } stored)
            {
                return Answer(stored, request);
            }
        }

        var version = await priceLists.FindPublishedForBranchAsync(request.BranchId, request.OrganisationId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<PricingResult>(BillingErrors.ConfigurationMissing(
                "No published price-list version prices the branch.", "branchId"));
        }

        if (version.EffectiveFrom > request.On)
        {
            // One version is published per branch and publishing retires its predecessor, so a version
            // published ahead of its first day leaves the branch with nothing in force until then
            // (decision OD-21). Said plainly rather than priced on a version that does not yet apply.
            return Result.Failure<PricingResult>(BillingErrors.ConfigurationMissing(
                $"The published price-list version pricing the branch applies from {version.EffectiveFrom:yyyy-MM-dd}, after that day.",
                "on"));
        }

        var calculated = await CalculateAsync(request, version, taxConfigurationVersionId: null, cancellationToken);
        if (calculated.IsFailure || request.Reference is null)
        {
            return calculated;
        }

        return await StoreAsync(request, calculated.Value, cancellationToken);
    }

    /// <summary>Prices a request against a named price-list version, published or not, storing nothing.</summary>
    /// <param name="request">What to price; its reference is ignored.</param>
    /// <param name="priceListVersionId">The version to price on.</param>
    /// <param name="taxConfigurationVersionId">The tax configuration version to take rates from, or null for the published one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result, or the refusal.</returns>
    public async Task<Result<PricingResult>> PreviewAsync(
        PricingRequest request,
        Guid priceListVersionId,
        Guid? taxConfigurationVersionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var version = await priceLists.FindVersionAsync(priceListVersionId, request.OrganisationId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<PricingResult>(BillingErrors.VersionNotFound);
        }

        return await CalculateAsync(request with { Reference = null }, version, taxConfigurationVersionId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PricingResult?> FindSnapshotAsync(Guid organisationId, string reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var stored = await snapshots.FindAsync(organisationId, reference.Trim(), cancellationToken);

        return stored is null ? null : PricingJson.ReadResult(stored.ResultJson);
    }

    private async Task<Result<PricingResult>> CalculateAsync(
        PricingRequest request,
        PriceListVersion version,
        Guid? taxConfigurationVersionId,
        CancellationToken cancellationToken)
    {
        var taxConfiguration = taxConfigurationVersionId is { } taxVersionId
            ? await taxConfigurations.FindAsync(taxVersionId, request.OrganisationId, cancellationToken)
            : await taxConfigurations.FindPublishedAsync(request.OrganisationId, cancellationToken);
        if (taxConfiguration is null || (taxConfigurationVersionId is null && taxConfiguration.EffectiveFrom > request.On))
        {
            return Result.Failure<PricingResult>(BillingErrors.ConfigurationMissing(
                "No tax configuration version is published for that day.", "on"));
        }

        var registration = (await registrations.ListForBranchAsync(request.BranchId, request.OrganisationId, cancellationToken))
            .FirstOrDefault(candidate => candidate.IsInForceOn(request.On));
        if (registration is null)
        {
            return Result.Failure<PricingResult>(BillingErrors.ConfigurationMissing(
                "The branch has no GST registration in force on that day.", "branchId"));
        }

        // The permission, and the second factor and the freshness its catalogue entry demands.
        var mayOverride = caller.HasPermission(BillingPermissions.OverridePrice)
                          && caller.MfaSatisfied
                          && StepUpFreshness.IsFresh(caller, clock, stepUp.Value);

        return PricingEngine.Calculate(request, version, taxConfiguration, registration, mayOverride, clock.UtcNow);
    }

    /// <summary>
    /// What a stored snapshot answers a request under its reference: the stored figure when it is the
    /// request that made it, the conflict otherwise. Compared as the request, not as text: the column is
    /// jsonb, which keeps the document and not its spelling, so the stored text is read back and written
    /// again the one way this build writes.
    /// </summary>
    private static Result<PricingResult> Answer(CalculationSnapshot stored, PricingRequest request)
        => string.Equals(
            PricingJson.Write(PricingJson.ReadRequest(stored.RequestJson)),
            PricingJson.Write(Canonical(request)),
            StringComparison.Ordinal)
            ? Result.Success(PricingJson.ReadResult(stored.ResultJson))
            : Result.Failure<PricingResult>(BillingErrors.SnapshotConflict);

    /// <summary>The request as it is stored and compared: the reference trimmed, nothing else touched.</summary>
    private static PricingRequest Canonical(PricingRequest request) => request with { Reference = request.Reference?.Trim() };

    private async Task<Result<PricingResult>> StoreAsync(PricingRequest request, PricingResult result, CancellationToken cancellationToken)
    {
        var reference = request.Reference!.Trim();
        var created = CalculationSnapshot.Create(
            ids.NewId(), request.OrganisationId, request.BranchId, reference,
            result.PriceListVersionId, result.TaxConfigurationVersionId, result.GstRegistrationId,
            PricingJson.Write(Canonical(request)), PricingJson.Write(result), result.CalculatedAt, caller.IsAuthenticated ? caller.UserId : null);
        if (created.IsFailure)
        {
            return Result.Failure<PricingResult>(created.Error);
        }

        snapshots.Add(created.Value);
        var saved = await snapshots.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            // Somebody stored a calculation under this reference between the read and the write. Theirs
            // is the figure of record, and it is answered exactly as it would have been had the read seen
            // it: the same request gets the stored figure, a different one gets the conflict.
            var winner = await snapshots.FindAsync(request.OrganisationId, reference, cancellationToken);

            return winner is null
                ? Result.Failure<PricingResult>(saved.Error)
                : Answer(winner, request);
        }

        var overridden = result.Lines.Where(line => line.ApprovalExercised).ToArray();
        if (overridden.Length > 0)
        {
            await BillingAudit.RecordAsync(
                audit, OverriddenAction, BillingAudit.CalculationSnapshotEntity, created.Value.Id,
                $"{overridden.Length} line(s) of calculation '{reference}' went beyond the catalogue or a rule's threshold "
                + "on the caller's billing.override_price.",
                string.Join("; ", overridden.Select(line => LineReason(request, line))),
                null,
                overridden.Select(OverrideSnapshot.Of).ToArray(),
                cancellationToken);
        }

        return Result.Success(result);
    }

    private static string LineReason(PricingRequest request, PricedLine line)
    {
        var asked = request.Lines.First(candidate => candidate.LineKey == line.LineKey);
        var reason = asked.Override?.Reason ?? asked.Discount?.Reason ?? string.Empty;

        return $"{line.LineKey}: {reason}";
    }
}

/// <summary>What the audit trail records about an overridden line: the variance, never the customer.</summary>
internal sealed record OverrideSnapshot(
    string LineKey,
    string ItemCode,
    decimal CatalogueRate,
    decimal AppliedRate,
    decimal Variance,
    decimal VariancePercent,
    string? DiscountRule,
    decimal? DiscountValue)
{
    public static OverrideSnapshot Of(PricedLine line)
        => new(
            line.LineKey, line.ItemCode, line.CatalogueRate, line.AppliedRate, line.Variance.Amount, line.VariancePercent,
            line.Discount?.RuleCode, line.Discount?.Value);
}
