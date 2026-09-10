using Microsoft.Extensions.Logging;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Re-checks the published catalogue's cross-module references after another module changed one of them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists at all.</strong> INV-MTV-06 says a published catalogue version's service type points at
/// a measurement template that has a published version, and both directions are guarded before the write — a
/// publication refuses a service type whose template has none, and a retirement refuses while a published
/// catalogue points at the template. The two guards live in <em>different modules</em>, each reading the other
/// through a contract and then writing to its own schema, so a catalogue publication and a template retirement
/// that overlap can each observe the other's pre-write state and both commit. Closing that window would need the
/// two writes serialised across the module boundary, which is precisely what <strong>G-6</strong> says this
/// codebase does not do: continued validity of a cross-module reference is <em>reconciled by events</em>. This is
/// that reconciliation (issue #91).
/// </para>
/// <para>
/// <strong>It asks the same question publication asks.</strong> Not a copy of the rule — the same
/// <see cref="CatalogPublicationCheck"/>, so a version that publishes cleanly and a version that reconciles
/// cleanly mean the same thing. Today only Customers registers a validator, so in practice this re-checks link 1;
/// as Orders, Billing and the QC module register theirs it re-checks those too, without a line changing here.
/// </para>
/// <para>
/// <strong>What it does not do is block.</strong> It records what it found and nothing else: a breach does not
/// flip <c>notOrderable</c>, does not refuse an order and does not retire the catalogue. Whether the counter
/// should be stopped from ordering a service whose template has gone is a product decision — <c>OD-18</c> in
/// <c>docs/prd/assumptions-and-open-decisions.md</c> — and inventing an answer here would be inventing a way for
/// a shop to stop taking orders overnight because an administrator retired the wrong template.
/// </para>
/// <para>
/// <strong>Idempotent by construction.</strong> Delivery is at least once, and a redelivery re-runs the same
/// check against the same state: an open breach that is still found stays exactly as it was, including its
/// detection time, which is what an administrator reads as how long the shop was exposed. A partial unique index
/// over the version, code and target — filtered on unresolved — is what makes that a fact about the database
/// rather than a hope about this class.
/// </para>
/// </remarks>
/// <param name="store">The catalogue store, for the published version.</param>
/// <param name="breaches">The breach record.</param>
/// <param name="check">What every registered validator says about a version.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="logger">The logger.</param>
public sealed class CatalogReconciler(
    ICatalogStore store,
    ICatalogReferenceBreachStore breaches,
    CatalogPublicationCheck check,
    IClock clock,
    IIdGenerator ids,
    ILogger<CatalogReconciler> logger)
{
    private static readonly Action<ILogger, Guid, string, Exception?> NothingPublished =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Debug,
            new EventId(2921, nameof(NothingPublished)),
            "Nothing to reconcile for organisation {OrganisationId} after {EventType}: no published catalogue.");

    private static readonly Action<ILogger, Guid, int, int, string, Exception?> Reconciled =
        LoggerMessage.Define<Guid, int, int, string>(
            LogLevel.Information,
            new EventId(2922, nameof(Reconciled)),
            "Reconciled catalogue version {VersionId}: {Opened} breach(es) opened, {Resolved} resolved, "
            + "after {EventType}.");

    private static readonly Action<ILogger, Guid, string, Exception?> CheckUnavailable =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(2923, nameof(CheckUnavailable)),
            "Could not reconcile catalogue version {VersionId} after {EventType}; nothing was concluded.");

    /// <summary>
    /// Re-checks one organisation's published catalogue and stages what changed.
    /// </summary>
    /// <remarks>
    /// <strong>Stages; does not save.</strong> It is called from the delivery of an integration event, and the
    /// dispatcher commits its writes with the inbox row that records the delivery ran.
    /// </remarks>
    /// <param name="organisationId">The organisation whose catalogue to re-check.</param>
    /// <param name="becauseOf">The wire name of the event that triggered the check, kept on what it opens.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What changed.</returns>
    /// <exception cref="InvalidOperationException">A validator could not answer, so nothing is concluded.</exception>
    public async Task<CatalogReconciliation> ReconcileAsync(
        Guid organisationId,
        string becauseOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(becauseOf);

        var published = await store.FindPublishedAsync(organisationId, cancellationToken);

        if (published is null)
        {
            // Nothing is orderable, so nothing can be stranded. A shop between catalogues is a real state.
            NothingPublished(logger, organisationId, becauseOf, null);
            return CatalogReconciliation.Nothing;
        }

        var report = await check.RunAsync(published, cancellationToken);

        if (report.IsFailure)
        {
            // "We could not check" is not "we checked and it is fine", so nothing is opened and — more
            // importantly — nothing standing is closed. Thrown rather than swallowed: the delivery fails, the
            // outbox retries it with backoff, and a validator that stays down dead-letters the message where
            // somebody is alerted, instead of leaving a silent gap in the reconciliation.
            CheckUnavailable(logger, published.Id, becauseOf, null);

            throw new InvalidOperationException(
                $"A catalogue validator could not answer while reconciling version {published.Id} after "
                + $"'{becauseOf}', so no conclusion was drawn: {report.Error.Code}.");
        }

        var found = report.Value.Findings
            .Where(finding => finding.Finding.Severity == CatalogFindingSeverity.Error)
            .DistinctBy(finding => Key(finding.Finding.Code, finding.Finding.Target), StringComparer.Ordinal)
            .ToDictionary(
                finding => Key(finding.Finding.Code, finding.Finding.Target),
                StringComparer.Ordinal);

        var standing = await breaches.ListOpenAsync(published.Id, cancellationToken);
        var now = clock.UtcNow;
        var resolved = 0;

        foreach (var breach in standing)
        {
            if (found.ContainsKey(Key(breach.Code, breach.Target)))
            {
                continue;
            }

            breach.Resolve(now, becauseOf);
            resolved++;
        }

        var alreadyOpen = standing
            .Where(breach => breach.IsOpen)
            .Select(breach => Key(breach.Code, breach.Target))
            .ToHashSet(StringComparer.Ordinal);

        var opened = 0;

        foreach (var (key, finding) in found)
        {
            if (alreadyOpen.Contains(key))
            {
                continue;
            }

            breaches.Add(new CatalogReferenceBreach(
                ids.NewId(),
                organisationId,
                published.Id,
                finding.Finding.Code,
                finding.Finding.Target ?? string.Empty,
                finding.Finding.Message,
                finding.Validator,
                becauseOf,
                now));

            opened++;
        }

        Reconciled(logger, published.Id, opened, resolved, becauseOf, null);

        return new CatalogReconciliation(published.Id, opened, resolved);
    }

    /// <summary>
    /// What decides whether a finding is the same breach as one already standing.
    /// </summary>
    /// <remarks>
    /// The code and the target, never the message: a validator that reworded its sentence would otherwise close
    /// every breach it had open and reopen all of them with a new detection time, wiping the one fact the record
    /// exists to carry. The two are joined by a character a target cannot contain, so a code ending where a target
    /// begins cannot collide with a different pair.
    /// </remarks>
    private static string Key(string code, string? target) => $"{code} {target ?? string.Empty}";
}

/// <summary>What one reconciliation changed.</summary>
/// <param name="CatalogVersionId">The published version that was checked, or null when none is published.</param>
/// <param name="Opened">How many breaches were newly opened.</param>
/// <param name="Resolved">How many standing breaches were found no longer to hold.</param>
public sealed record CatalogReconciliation(Guid? CatalogVersionId, int Opened, int Resolved)
{
    /// <summary>No published catalogue, so nothing to check and nothing to conclude.</summary>
    public static CatalogReconciliation Nothing { get; } = new(null, 0, 0);
}
