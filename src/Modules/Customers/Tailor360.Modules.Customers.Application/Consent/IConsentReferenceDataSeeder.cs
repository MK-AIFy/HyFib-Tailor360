namespace Tailor360.Modules.Customers.Application.Consent;

/// <summary>
/// Writes the reference data the Customers module owns: the consent purposes a shop asks about.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent and safe to run against a live database, like the Identity seeder beside it. Running it
/// twice changes nothing; running it after a release that renamed a purpose corrects the name and
/// leaves every consent record naming that purpose exactly as it was.
/// </para>
/// <para>
/// <strong>It seeds no wording, and that is the point.</strong> A consent record must name a wording
/// version, so a purpose with none cannot have consent recorded against it at all — the shop is
/// stopped at the counter until an Owner publishes words for it. That is deliberate:
/// <c>docs/prd/configurable-vs-fixed.md</c> row 75 requires the wording to be *reviewed before
/// publication*, and <c>docs/nfr/data-classification.md</c> section 4.1 is emphatic that what this Act
/// requires of a business this size is unsettled (<strong>DC-01</strong>) and "must not be presented as
/// settled anywhere else". Shipping invented notice text as seeded data would be exactly that: a legal
/// notice nobody reviewed, in production, with customers agreeing to it.
/// </para>
/// <para>
/// So the seeder puts the <em>register</em> in place — the five purposes, their names and what each
/// governs, all of which are factual and taken from section 4.2 — and leaves the words to the person
/// who is accountable for them. DC-01 becomes something the system enforces rather than something a
/// document mentions.
/// </para>
/// </remarks>
public interface IConsentReferenceDataSeeder
{
    /// <summary>Creates or refreshes the consent purposes for an organisation.</summary>
    /// <param name="organisationId">The organisation whose purposes are being seeded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the run did, so that an operator has evidence rather than a promise.</returns>
    Task<ConsentPurposeSeedOutcome> SeedConsentPurposesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);
}

/// <summary>What one seeding run did.</summary>
/// <param name="PurposesCreated">Purposes that did not exist and were created.</param>
/// <param name="PurposesUpdated">Purposes whose name or description changed.</param>
/// <param name="PurposesUnchanged">Purposes already matching their definition.</param>
/// <param name="PurposesAwaitingWording">
/// Purposes with no published wording. Consent cannot be recorded against any of them until an Owner
/// publishes words, so this number is the one an operator acts on after a first install.
/// </param>
public sealed record ConsentPurposeSeedOutcome(
    int PurposesCreated,
    int PurposesUpdated,
    int PurposesUnchanged,
    int PurposesAwaitingWording);
