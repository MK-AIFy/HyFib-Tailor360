using System.Globalization;
using Npgsql;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// Forces the interleaving a concurrency test is about, instead of hoping the scheduler produces it.
/// </summary>
/// <remarks>
/// <para>
/// The race that matters in this schema is always the same shape: two requests read a row that still
/// permits what they are about to do, and exactly one of them must be the one that writes. Left to the
/// scheduler the first request usually finishes before the second has read, every later request is
/// then refused by the check in memory, and the test passes whether or not the database arbitrates at
/// all — so it would pass with the arbitration removed, which is not a test.
/// </para>
/// <para>
/// The gate holds the contested row in a transaction that writes nothing. Every racer gets past its
/// read while the row still permits the operation, then stops at its update; the lock is released only
/// once all of them are waiting there, so the one thing that can decide the winner is the condition
/// the code under test puts on its write.
/// </para>
/// </remarks>
internal sealed class RowGate : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly string _connectionString;

    private RowGate(NpgsqlConnection connection, NpgsqlTransaction transaction, string connectionString)
    {
        _connection = connection;
        _transaction = transaction;
        _connectionString = connectionString;
    }

    /// <summary>Takes and holds the row named by <paramref name="id"/>.</summary>
    /// <param name="connectionString">The database the row is in.</param>
    /// <param name="qualifiedTable">The schema-qualified table, for example <c>identity.sessions</c>.</param>
    /// <param name="id">The row's primary key.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<RowGate> HoldAsync(
        string connectionString,
        string qualifiedTable,
        Guid id,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The table name is a constant at every call site — these are tests naming their own schema,
        // not a query built from anything a caller supplied.
        await using var hold = new NpgsqlCommand(
            $"SELECT id FROM {qualifiedTable} WHERE id = @id FOR UPDATE", connection, transaction);

        hold.Parameters.AddWithValue("id", id);

        if (await hold.ExecuteScalarAsync(cancellationToken) is null)
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw new InvalidOperationException($"There is no row {id} in {qualifiedTable} to hold.");
        }

        return new RowGate(connection, transaction, connectionString);
    }

    /// <summary>
    /// Waits until <paramref name="count"/> other connections are waiting on the held row, then lets
    /// them go.
    /// </summary>
    /// <remarks>
    /// It observes down its own connection, and outside any transaction, because PostgreSQL caches the
    /// activity statistics per transaction: polling from inside the gate's own transaction returns the
    /// snapshot taken at the first read, over and over, and reports every racer as absent however long
    /// it waits. That cost an afternoon, so it is written down here rather than rediscovered.
    /// </remarks>
    /// <param name="count">How many racers to wait for.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task ReleaseWhenWaitingAsync(int count, CancellationToken cancellationToken)
    {
        await using (var observer = new NpgsqlConnection(_connectionString))
        {
            await observer.OpenAsync(cancellationToken);

            var deadline = DateTime.UtcNow.AddSeconds(30);
            var blocked = 0;

            while (blocked < count)
            {
                await using (var waiting = new NpgsqlCommand(
                    """
                    SELECT count(*) FROM pg_stat_activity
                     WHERE datname = current_database()
                       AND pid <> pg_backend_pid()
                       AND wait_event_type = 'Lock'
                    """,
                    observer))
                {
                    blocked = Convert.ToInt32(
                        await waiting.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                }

                if (blocked >= count)
                {
                    break;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new InvalidOperationException(
                        $"Only {blocked} of {count} racers reached the held row within thirty seconds.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
        }

        await _transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
