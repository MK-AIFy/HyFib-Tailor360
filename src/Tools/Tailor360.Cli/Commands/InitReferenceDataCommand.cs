using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Cli.Commands;

/// <summary>
/// Creates or refreshes the reference data every installation needs. It is idempotent and safe to run
/// in production, and it is the only seeding command that is; anything synthetic belongs to
/// <c>seed-synthetic</c>, which refuses to run there at all.
/// </summary>
public static class InitReferenceDataCommand
{
    /// <summary>How many future months of audit partitions are kept ahead.</summary>
    public const int AuditPartitionsAhead = 3;

    /// <summary>Builds the command.</summary>
    public static Command Create()
    {
        var organisation = new Option<Guid>("--organisation")
        {
            Description =
                "The organisation to seed roles for. Defaults to the single-organisation identifier.",
            DefaultValueFactory = _ => OrganisationDefaults.OrganisationId,
        };

        var command = new Command(
            "init-reference-data",
            "Create or refresh the reference data every installation needs. Idempotent and production-safe.")
        {
            organisation,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var organisationId = parseResult.GetValue(organisation);

            using var host = CliHost.Build();
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var seeder = scope.ServiceProvider.GetRequiredService<IIdentityReferenceDataSeeder>();
            var consent = scope.ServiceProvider.GetRequiredService<IConsentReferenceDataSeeder>();
            var catalogue = scope.ServiceProvider.GetRequiredService<ICatalogReferenceDataSeeder>();
            var templates = scope.ServiceProvider
                .GetRequiredService<IMeasurementTemplateReferenceDataSeeder>();
            var billing = scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>();

            Console.WriteLine($"Environment: {EnvironmentGuard.CurrentEnvironment}");

            // Partitions are created ahead of time rather than on first write, so that the first audit
            // entry after midnight on the first of a month is not the thing that discovers a missing
            // partition.
            for (var month = 0; month <= AuditPartitionsAhead; month++)
            {
                await context.Database.ExecuteSqlAsync(
                    $"SELECT platform.ensure_audit_partition(now() + ({month} * interval '1 month'))",
                    cancellationToken);
            }

            Console.WriteLine(
                $"Audit partitions ensured for the current month and the next {AuditPartitionsAhead}.");

            var roles = await seeder.SeedSystemRolesAsync(organisationId, cancellationToken);

            Console.WriteLine($"Organisation: {organisationId}");
            Console.WriteLine(
                $"System roles: {roles.RolesCreated} created, {roles.RolesUpdated} updated, "
                + $"{roles.RolesUnchanged} already current.");
            Console.WriteLine(
                $"Default grants: {roles.PermissionsGranted} added, {roles.PermissionsRevoked} removed "
                + "to match docs/security/permission-matrix.md.");
            var purposes = await consent.SeedConsentPurposesAsync(organisationId, cancellationToken);

            Console.WriteLine(
                $"Consent purposes: {purposes.PurposesCreated} created, {purposes.PurposesUpdated} "
                + $"updated, {purposes.PurposesUnchanged} already current.");

            if (purposes.PurposesAwaitingWording > 0)
            {
                // Not a warning about the seeder; a statement about what the shop cannot do yet. A
                // consent record names a wording version, so a purpose without one cannot be consented
                // to at all — which is deliberate, and is DC-01 enforced rather than mentioned.
                Console.WriteLine(
                    $"  {purposes.PurposesAwaitingWording} of them have no published wording, so "
                    + "consent cannot be recorded against them yet. An Owner publishes the words a "
                    + "customer is actually read, after review — this command does not invent them "
                    + "(docs/nfr/data-classification.md section 4.1, DC-01).");
            }

            var catalog = await catalogue.SeedInitialCatalogAsync(organisationId, cancellationToken);

            Console.WriteLine(catalog.Created
                ? $"Catalogue: version {catalog.VersionNumber} drafted with {catalog.CategoryCount} "
                  + $"categories and {catalog.ServiceTypeCount} service types."
                : $"Catalogue: version {catalog.VersionNumber} already exists; nothing was changed.");

            if (catalog.AwaitingPublication)
            {
                // Not a warning about the seeder; a statement about what the shop cannot do yet. The
                // same shape as the consent purposes above, and for the same reason: publishing is an
                // Owner's act with a second factor and a stated reason, and the hierarchy itself is
                // open decision OD-CAT-01. A command-line tool answering to nobody must not settle it.
                Console.WriteLine(
                    "  No catalogue version is published, so nothing can be ordered yet. Before an "
                    + "Owner publishes: set branch availability on each category and service type (an "
                    + "empty set means offered nowhere, which is what the seed leaves — OD-CAT-04), "
                    + "and supply the five links each service type carries. The draft's notes say the "
                    + "same, where the administration screens show them.");
            }

            var measurement = await templates.SeedTemplatesAsync(organisationId, cancellationToken);

            Console.WriteLine(measurement.Created
                ? $"Measurement templates: {measurement.TemplateCount} drafted with "
                  + $"{measurement.FieldCount} fields between them."
                : $"Measurement templates: {measurement.TemplateCount} already exist; nothing was changed.");

            if (measurement.AwaitingPublication > 0)
            {
                // The same shape as the consent purposes and the catalogue above, and for the same reason. Every
                // seeded field set is marked "proposed and to be confirmed" in docs/prd/measurement-templates.md,
                // its bounds and inch steps are open decisions OD-MEA-01, OD-MEA-07 and OD-MEA-08, and business
                // review of each template is an acceptance criterion of issue #27. Publishing wants a permission,
                // a second factor and a stated reason; this command holds none of them.
                Console.WriteLine(
                    $"  {measurement.AwaitingPublication} of them have no published version, so nothing can be "
                    + "measured against them yet. An Owner reviews each field set with the Tailor Master — the "
                    + "field list, the hard bounds, the confirmation bands and the inch steps — and publishes it "
                    + "with a reason. The draft's own notes say which section of the specification it came from.");
            }

            var paymentModes = await billing.SeedPaymentModesAsync(organisationId, cancellationToken);

            Console.WriteLine(
                $"Payment modes: {paymentModes.ModesCreated} created, {paymentModes.ModesUnchanged} already present "
                + "and left as the Owner set them.");

            if (paymentModes.ModesCreated > 0)
            {
                // The same shape as the seeds above: the flags a mode starts with — which modes carry a
                // reference, which may pay a refund — are the seed's starting position for a shop with
                // standalone terminals and no gateway (OD-03), and an Owner sets them.
                Console.WriteLine(
                    "  The modes start available at every branch, with cash the one mode a refund may be paid "
                    + "through. An Owner sets each mode's reference, provider and refund flags and any branch "
                    + "restriction, and renames a mode; this command never changes any of it back.");
            }

            return ExitCodes.Success;
        });

        return command;
    }
}
