namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>
/// Creates or refreshes the reference data Billing needs at every installation: the payment modes.
/// Idempotent, and safe to run in production: it defines a mode that is missing and touches nothing on one
/// that exists — not the name, not a flag, not the branch restriction, not the active state — because all
/// of those are the Owner's to set through the audited route, and the seeder answers to nobody.
/// </summary>
public interface IBillingReferenceDataSeeder
{
    /// <summary>Seeds the payment modes for one organisation.</summary>
    Task<PaymentModeSeedOutcome> SeedPaymentModesAsync(Guid organisationId, CancellationToken cancellationToken = default);
}

/// <summary>What one run did.</summary>
/// <param name="ModesCreated">Modes that did not exist.</param>
/// <param name="ModesUpdated">Always zero: an existing mode is the Owner's and the seeder never changes one. Kept so the outcome reads like the other seeders'.</param>
/// <param name="ModesUnchanged">Modes already current.</param>
public sealed record PaymentModeSeedOutcome(int ModesCreated, int ModesUpdated, int ModesUnchanged);
