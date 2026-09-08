using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tailor360.Platform.Persistence.Outbox;

/// <summary>
/// Holds a claim open while a handler runs. Without renewal, a handler that outlives the lease — an
/// external provider stalling is enough — lets a second dispatcher claim the same message and invoke
/// the same handler concurrently. The inbox row cannot prevent that, because it is only written after
/// the handler returns, so the two invocations would both find no inbox row and both proceed.
///
/// Renewal runs on its own scope and connection: the delivery path is using the caller's context, and
/// two commands on one connection would serialise or fail.
/// </summary>
public sealed class OutboxLeaseRenewal : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _loop;

    private OutboxLeaseRenewal(
        IServiceScopeFactory scopeFactory,
        Guid messageId,
        string owner,
        TimeSpan leaseDuration,
        Type contextType,
        string schema)
        => _loop = RenewAsync(
            scopeFactory, messageId, owner, leaseDuration, contextType, schema, _stopping.Token);

    /// <summary>Starts renewing the lease on a message until the returned object is disposed.</summary>
    /// <param name="scopeFactory">Creates a scope per renewal.</param>
    /// <param name="messageId">The message whose lease to hold.</param>
    /// <param name="owner">The dispatcher instance that holds it.</param>
    /// <param name="leaseDuration">How long each renewal extends the lease by.</param>
    /// <param name="contextType">
    /// The context that owns the outbox the message came from. Each module has its own, so a renewal
    /// aimed at the wrong one would silently update nothing and let the lease lapse (issue #77).
    /// </param>
    /// <param name="schema">That module's schema, already validated by the dispatcher.</param>
    /// <returns>The renewal, which stops when it is disposed.</returns>
    public static OutboxLeaseRenewal Start(
        IServiceScopeFactory scopeFactory,
        Guid messageId,
        string owner,
        TimeSpan leaseDuration,
        Type contextType,
        string schema)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(contextType);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        return new OutboxLeaseRenewal(
            scopeFactory, messageId, owner, leaseDuration, contextType, schema);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // Expected: the loop is cancelled by disposal.
        }

        _stopping.Dispose();
    }

    private static async Task RenewAsync(
        IServiceScopeFactory scopeFactory,
        Guid messageId,
        string owner,
        TimeSpan leaseDuration,
        Type contextType,
        string schema,
        CancellationToken cancellationToken)
    {
        // A third of the lease gives two chances to renew before it lapses, so one slow renewal does
        // not hand the message to another dispatcher.
        var interval = leaseDuration / 3;

        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                using var scope = scopeFactory.CreateScope();
                var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);

                // Conditional on still owning it: a dispatcher that already lost the lease must not
                // take it back from whoever now holds it.
                // The schema was validated by the dispatcher before the claim; the values are
                // parameters.
                var renew = $$"""
                    UPDATE {{schema}}.outbox_messages
                       SET lease_expires_at = now() + ({0} * interval '1 second')
                     WHERE id = {1} AND lease_owner = {2}
                    """;

                await context.Database.ExecuteSqlRawAsync(
                    renew,
                    [leaseDuration.TotalSeconds, messageId, owner],
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Delivery finished.
        }
    }
}
