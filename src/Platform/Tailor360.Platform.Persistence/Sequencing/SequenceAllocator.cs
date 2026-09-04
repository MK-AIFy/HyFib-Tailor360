using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Tailor360.Platform.Abstractions.Sequencing;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Platform.Persistence.Sequencing;

/// <summary>
/// Allocates gap-free document numbers. The allocation runs inside the caller's transaction and takes a
/// row lock, so a rolled-back invoice returns its number and a statutory series has no holes. That is
/// also why a PostgreSQL sequence is not used: sequences deliberately survive rollback and leave gaps.
/// The row lock serialises allocation per scope, which is the intended cost.
/// </summary>
/// <param name="context">The platform context.</param>
public sealed class SequenceAllocator(PlatformDbContext context) : ISequenceAllocator
{
    /// <inheritdoc />
    public async Task<long> NextAsync(
        string sequenceKey,
        string scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        const string sql = """
            INSERT INTO platform.sequences (sequence_key, scope, next_value, updated_at)
            VALUES (@key, @scope, 2, now())
            ON CONFLICT (sequence_key, scope) DO UPDATE
                SET next_value = platform.sequences.next_value + 1,
                    updated_at = now()
            RETURNING next_value - 1;
            """;

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
