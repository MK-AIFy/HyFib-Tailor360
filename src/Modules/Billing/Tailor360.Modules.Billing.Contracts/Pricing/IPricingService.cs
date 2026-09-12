using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Contracts.Pricing;

/// <summary>
/// Prices lines against the configuration in force at a branch on a day: the published price-list
/// version pricing the branch, the published tax configuration and the branch's GST registration.
/// The only way another module obtains a price.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The contract takes no order entity</strong> (ARCH-010, decision OD-11). A caller keys each
/// line by its own reference — a garment job, a draft garment — and receives the line back under that
/// key with every component the invoice will print. Billing learns nothing about what the key means.
/// </para>
/// <para>
/// <strong>Deterministic.</strong> Identical lines against identical configuration produce identical
/// figures, in whatever order the lines arrive; a request carrying a <see cref="PricingRequest.Reference"/>
/// is answered once and stored as a calculation snapshot, and asking again with the same reference
/// returns the stored result whatever has been published since (<c>docs/architecture/conventions.md</c>
/// section 1.2, plan D10, <c>INV-INV-03</c>). A request without a reference is priced and forgotten.
/// </para>
/// <para>
/// <strong>Missing configuration is a refusal</strong>, never a guess: no price-list version in force at
/// the branch on the day, no published tax configuration, no registration in force, an item whose tax
/// code the configuration does not hold — each answers <c>billing.configuration-missing</c> naming what.
/// </para>
/// </remarks>
public interface IPricingService
{
    /// <summary>Prices a request, storing the result under its reference when it carries one.</summary>
    /// <param name="request">What to price.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result, or why it could not be produced.</returns>
    Task<Result<PricingResult>> PriceAsync(PricingRequest request, CancellationToken cancellationToken = default);

    /// <summary>The result stored under a reference, exactly as it was calculated.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="reference">The caller's reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored result, or null when nothing was stored under that reference.</returns>
    Task<PricingResult?> FindSnapshotAsync(Guid organisationId, string reference, CancellationToken cancellationToken = default);
}
