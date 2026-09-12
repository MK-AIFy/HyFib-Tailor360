using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Infrastructure.Payments;

/// <summary>Seeds the payment modes: an upsert by code, in the shape of the consent-purpose seeder.</summary>
public sealed class BillingReferenceDataSeeder(BillingDbContext context, IClock clock, IIdGenerator ids) : IBillingReferenceDataSeeder
{
    /// <inheritdoc />
    public async Task<PaymentModeSeedOutcome> SeedPaymentModesAsync(Guid organisationId, CancellationToken cancellationToken = default)
    {
        var existing = await context.PaymentModes
            .Where(mode => mode.OrganisationId == organisationId)
            .ToListAsync(cancellationToken);
        var byCode = existing.ToDictionary(mode => mode.Code, StringComparer.Ordinal);
        var now = clock.UtcNow;
        var created = 0;
        var unchanged = 0;

        foreach (var seeded in SeededPaymentModes.All)
        {
            if (!byCode.TryGetValue(seeded.Code, out var mode))
            {
                var defined = PaymentMode.Define(ids.NewId(), organisationId, seeded.Code, seeded.Details, now);
                if (defined.IsFailure)
                {
                    // The set is a compile-time constant held to the same rule the domain applies; failing
                    // here means the two drifted, which is worth stopping an install for.
                    throw new InvalidOperationException($"The seeded payment mode '{seeded.Code}' is not one the domain accepts: {defined.Error.Code}");
                }

                context.PaymentModes.Add(defined.Value);
                byCode[seeded.Code] = defined.Value;
                created++;
                continue;
            }

            // Everything on an existing mode — the name on the button, the flags, the branches, the active
            // state — is the Owner's, set through the audited route; a run never moves any of it back.
            unchanged++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return new PaymentModeSeedOutcome(created, 0, unchanged);
    }
}
