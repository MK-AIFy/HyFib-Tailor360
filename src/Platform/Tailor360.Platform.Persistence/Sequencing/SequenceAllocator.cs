using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Tailor360.Platform.Abstractions.Sequencing;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Platform.Persistence.Sequencing;

/// <summary>
/// Allocates document numbers from <c>platform.sequences</c> with one <c>INSERT … ON CONFLICT DO UPDATE</c>,
/// which takes the sequence row's lock for the rest of whatever transaction it runs in. That is why a
/// PostgreSQL sequence is not used: sequences deliberately survive rollback and leave gaps. Which
/// transaction it runs in is the caller's choice, and the difference is the whole point of the two
/// overloads: on the platform context's own connection the allocation commits at once (the transaction
/// the module opened on its own context is not this one, whatever the caller assumes), while on a
/// transaction the caller passes it commits or rolls back with the document.
/// </summary>
/// <param name="context">The platform context.</param>
public sealed class SequenceAllocator(PlatformDbContext context) : ISequenceAllocator, ITransactionalSequenceAllocator
{
    private const string Sql = """
        INSERT INTO platform.sequences (sequence_key, scope, next_value, updated_at)
        VALUES (@key, @scope, 2, now())
        ON CONFLICT (sequence_key, scope) DO UPDATE
            SET next_value = platform.sequences.next_value + 1,
                updated_at = now()
        RETURNING next_value - 1;
        """;

    /// <inheritdoc />
    public async Task<long> NextAsync(
        string sequenceKey,
        string scope,
        DbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction is not NpgsqlTransaction npgsqlTransaction || npgsqlTransaction.Connection is not { } connection)
        {
            throw new InvalidOperationException("The sequence is allocated inside an open PostgreSQL transaction.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = Sql;
        command.Transaction = npgsqlTransaction;
        command.Parameters.Add(new NpgsqlParameter("key", sequenceKey));
        command.Parameters.Add(new NpgsqlParameter("scope", scope));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<long> NextAsync(
        string sequenceKey,
        string scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        const string sql = Sql;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("key", sequenceKey));
            command.Parameters.Add(new NpgsqlParameter("scope", scope));

            if (context.Database.CurrentTransaction is { } transaction)
            {
                command.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            }

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (opened)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }
}
