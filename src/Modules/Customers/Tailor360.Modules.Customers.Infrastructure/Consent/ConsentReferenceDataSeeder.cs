using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Customers.Infrastructure.Consent;

/// <summary>
/// Puts the consent register in place, and deliberately leaves the words to whoever is accountable
/// for them.
/// </summary>
/// <remarks>
/// See <see cref="IConsentReferenceDataSeeder"/> for why no wording is published here. In short: a
/// purpose with no wording cannot have consent recorded against it, so DC-01 becomes a thing the
/// system enforces at the counter rather than a note in a document.
/// </remarks>
/// <param name="context">The module's context.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class ConsentReferenceDataSeeder(
    CustomersDbContext context,
    IClock clock,
    IIdGenerator ids)
    : IConsentReferenceDataSeeder
{
    /// <inheritdoc />
    public async Task<ConsentPurposeSeedOutcome> SeedConsentPurposesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.ConsentPurposes
            .Include(purpose => purpose.Wordings)
            .Where(purpose => purpose.OrganisationId == organisationId)
            .ToListAsync(cancellationToken);

        var byKey = existing.ToDictionary(purpose => purpose.Key, StringComparer.Ordinal);
        var now = clock.UtcNow;
        var created = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var seeded in SeededConsentPurposes.All)
        {
            if (!byKey.TryGetValue(seeded.Key, out var purpose))
            {
                var defined = ConsentPurpose.Define(
                    ids.NewId(), organisationId, seeded.Key, seeded.Name, seeded.Description, now);

                // The set is a compile-time constant held to the same rule the endpoint applies, and a
                // unit test asserts every key in it is one Define accepts. Failing here would mean the
                // two had drifted, which is worth stopping an install for rather than seeding half a
                // register.
                if (defined.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"The seeded consent purpose '{seeded.Key}' is not one the domain accepts: "
                        + defined.Error.Code);
                }

                context.ConsentPurposes.Add(defined.Value);
                byKey[seeded.Key] = defined.Value;
                created++;
                continue;
            }

            // Renaming a purpose corrects the register and touches no consent record: a record names
            // the key, and the key does not change.
            if (purpose.Rename(seeded.Name, seeded.Description, now, by: null))
            {
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        var awaitingWording = byKey.Values.Count(purpose => purpose.CurrentWordingVersion == 0);

        return new ConsentPurposeSeedOutcome(created, updated, unchanged, awaitingWording);
    }
}
