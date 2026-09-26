using Jarvis.Application.Conversations;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Jarvis.Infrastructure.Persistence;

public sealed class PostgresConversationRunLock(string connectionString,
    ILogger<PostgresConversationRunLock> logger) : IConversationRunLock
{
    public async ValueTask<IAsyncDisposable> AcquireAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            while (true)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@conversation_id, 0))";
                command.Parameters.AddWithValue("conversation_id", conversationId.ToString("N"));
                if (await command.ExecuteScalarAsync(cancellationToken) is true)
                    return new Lease(connection, conversationId, logger);

                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(NpgsqlConnection connection, Guid conversationId,
        ILogger logger) : IAsyncDisposable
    {
        private NpgsqlConnection? _connection = connection;

        public async ValueTask DisposeAsync()
        {
            var heldConnection = Interlocked.Exchange(ref _connection, null);
            if (heldConnection is null) return;

            try
            {
                await using var command = heldConnection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@conversation_id, 0))";
                command.Parameters.AddWithValue("conversation_id", conversationId.ToString("N"));
                command.CommandTimeout = 5;
                var released = await command.ExecuteScalarAsync(CancellationToken.None);
                if (released is not true)
                {
                    logger.LogError("PostgreSQL did not release the conversation run lock for {ConversationId}.",
                        conversationId);
                    NpgsqlConnection.ClearPool(heldConnection);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not release the conversation run lock for {ConversationId}.",
                    conversationId);
                NpgsqlConnection.ClearPool(heldConnection);
            }
            finally
            {
                await heldConnection.DisposeAsync();
            }
        }
    }
}
