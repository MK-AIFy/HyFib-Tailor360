using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Platform.Persistence.Contexts;

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
        TimeSpan leaseDuration)
        => _loop = RenewAsync(scopeFactory, messageId, owner, leaseDuration, _stopping.Token);

    /// <summary>Starts renewing the lease on a message until the returned object is disposed.</summary>
    public static OutboxLeaseRenewal Start(
        IServiceScopeFactory scopeFactory,
        Guid messageId,
        string owner,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return new OutboxLeaseRenewal(scopeFactory, messageId, owner, leaseDuration);
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
                var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

                // Conditional on still owning it: a dispatcher that already lost the lease must not
                // take it back from whoever now holds it.
                await context.Database.ExecuteSqlAsync(
                    $"""
                     UPDATE platform.outbox_messages
                        SET lease_expires_at = now() + ({leaseDuration.TotalSeconds} * interval '1 second')
                      WHERE id = {messageId} AND lease_owner = {owner}
                     """,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Delivery finished.
        }
    }
}
