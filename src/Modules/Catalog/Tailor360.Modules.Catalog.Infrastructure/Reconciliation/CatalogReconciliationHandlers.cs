using System.Text.Json;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Events;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Modules.Customers.Contracts.Events;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Modules.Catalog.Infrastructure.Reconciliation;

/// <summary>
/// What every reconciliation handler does, which is the same thing from three different events.
/// </summary>
/// <remarks>
/// <para>
/// One handler per event type, because that is the shape <see cref="IOutboxMessageHandler"/> has and because the
/// inbox de-duplicates on <see cref="IOutboxMessageHandler.HandlerName"/> — a single handler answering to three
/// names would need three registrations anyway, and three names is what makes "this delivery already ran" a
/// question with an answer.
/// </para>
/// <para>
/// <strong>Three events and not one.</strong> The race INV-MTV-06 leaves open has two orderings, and each is
/// closed by a different event. A template retirement committing after a catalogue publication was validated is
/// caught by the retirement event; a catalogue publication committing after a template retirement is caught by
/// the publication event, because the publication's own validation ran before the retirement landed. The template
/// publication event is the third, and it is what <em>closes</em> a breach: the shop fixed it by publishing a
/// template version, and nothing else would tell the catalogue so.
/// </para>
/// <para>
/// <strong>All three write into <c>catalog</c>.</strong> The schema is what the dispatcher uses to pick the
/// context the handler's writes and its inbox row commit on, and a breach recorded anywhere else could survive a
/// rollback of the delivery that found it.
/// </para>
/// </remarks>
/// <param name="reconciler">The reconciliation.</param>
public abstract class CatalogReconciliationHandler(CatalogReconciler reconciler) : IOutboxMessageHandler
{
    /// <inheritdoc />
    public abstract string EventType { get; }

    /// <inheritdoc />
    public abstract string HandlerName { get; }

    /// <inheritdoc />
    public string Schema => CatalogDbContext.SchemaName;

    /// <inheritdoc />
    public async Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var organisationId = OrganisationOf(delivery);

        await reconciler.ReconcileAsync(organisationId, delivery.EventType, cancellationToken);
    }

    /// <summary>Reads the one field every one of these events carries and this needs.</summary>
    /// <param name="delivery">The message.</param>
    /// <returns>The organisation whose catalogue to re-check.</returns>
    /// <remarks>
    /// Read as a bare object rather than deserialised into the event record. The record carries fields this does
    /// not use and a version of it in the consuming module would be a second copy of the producer's contract; a
    /// payload written before a field was added must also still be readable, which is what
    /// <c>additionalProperties</c> in the published schema promises.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The payload carries no usable organisation.</exception>
    private static Guid OrganisationOf(OutboxDelivery delivery)
    {
        using var document = JsonDocument.Parse(delivery.Payload);

        var organisationId = document.RootElement.TryGetProperty("organisationId", out var property)
            && property.TryGetGuid(out var value)
                ? value
                : Guid.Empty;

        return organisationId == Guid.Empty
            ? throw new InvalidOperationException(
                $"The payload of {delivery.EventType} message {delivery.MessageId} carries no organisationId, so "
                + "there is no catalogue to reconcile. The message is left to retry and then dead-letter rather "
                + "than treated as an organisation with nothing published.")
            : organisationId;
    }
}

/// <summary>
/// Reconciles after a measurement-template version was retired.
/// </summary>
/// <remarks>
/// The write that can strand a published catalogue: the retirement guard asked Catalog and then wrote to its own
/// schema, so a catalogue publication committing in that window was validated against a template this retirement
/// has since emptied.
/// </remarks>
/// <param name="reconciler">The reconciliation.</param>
public sealed class TemplateRetiredReconciliationHandler(CatalogReconciler reconciler)
    : CatalogReconciliationHandler(reconciler)
{
    /// <inheritdoc />
    public override string EventType => MeasurementTemplateVersionRetired.Type;

    /// <inheritdoc />
    public override string HandlerName => "catalog.reconcile-on-template-retired";
}

/// <summary>
/// Reconciles after a measurement-template version was published.
/// </summary>
/// <remarks>
/// The event that heals rather than breaks: publishing a version gives a stranded service type something to
/// measure by again, and a breach that no longer holds is closed rather than left standing for somebody to
/// dismiss by hand.
/// </remarks>
/// <param name="reconciler">The reconciliation.</param>
public sealed class TemplatePublishedReconciliationHandler(CatalogReconciler reconciler)
    : CatalogReconciliationHandler(reconciler)
{
    /// <inheritdoc />
    public override string EventType => MeasurementTemplateVersionPublished.Type;

    /// <inheritdoc />
    public override string HandlerName => "catalog.reconcile-on-template-published";
}

/// <summary>
/// Reconciles after this module published a catalogue version.
/// </summary>
/// <remarks>
/// <para>
/// The other ordering of the same race, and the reason this module consumes its own event. A publication is
/// validated and then committed; a template retirement that commits in between leaves the new catalogue
/// referencing a template with no published version, and the publication's own validation cannot have seen it.
/// </para>
/// <para>
/// It re-checks the version that is published <em>now</em> rather than the one the event names. Those are the
/// same thing in every ordinary case, and where they differ — a second publication landing before this delivery —
/// the currently published version is the one a counter is ordering from, which is the only one worth an answer.
/// </para>
/// </remarks>
/// <param name="reconciler">The reconciliation.</param>
public sealed class CatalogPublishedReconciliationHandler(CatalogReconciler reconciler)
    : CatalogReconciliationHandler(reconciler)
{
    /// <inheritdoc />
    public override string EventType => CatalogVersionPublished.Type;

    /// <inheritdoc />
    public override string HandlerName => "catalog.reconcile-on-catalogue-published";
}
