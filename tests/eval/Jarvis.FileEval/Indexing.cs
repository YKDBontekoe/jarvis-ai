using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jarvis.Application.Files;
using Jarvis.Domain.Files;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Jarvis.FileEval;

internal static class Indexing
{
    public static JarvisDbContext CreateDb(Workspace workspace, string? database = null) => new(
        new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(workspace.Connection(database ?? workspace.DbName), npgsql => npgsql.UseVector()).Options);

    /// <summary>Creates the scratch database for a chunking configuration from a migrated template (migrating takes a while).</summary>
    public static async Task CreateDatabaseAsync(Workspace workspace)
    {
        await using (var admin = new NpgsqlConnection(workspace.Connection("postgres")))
        {
            await admin.OpenAsync();
            var exists = await Scalar(admin, $"SELECT count(*) FROM pg_database WHERE datname = '{Workspace.TemplateDb}'") > 0;
            if (!exists)
            {
                await Execute(admin, $"CREATE DATABASE {Workspace.TemplateDb}");
                await using var template = CreateDb(workspace, Workspace.TemplateDb);
                await template.Database.MigrateAsync();
            }
        }
        NpgsqlConnection.ClearAllPools();
        await using (var admin = new NpgsqlConnection(workspace.Connection("postgres")))
        {
            await admin.OpenAsync();
            await Execute(admin, $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{Workspace.TemplateDb}' AND pid <> pg_backend_pid()");
            await Execute(admin, $"DROP DATABASE IF EXISTS {workspace.DbName} WITH (FORCE)");
            await Execute(admin, $"CREATE DATABASE {workspace.DbName} TEMPLATE {Workspace.TemplateDb}");
        }
    }

    public static async Task IndexAsync(Workspace workspace, string chunker, int length, int overlap, int foreignEvery)
    {
        await CreateDatabaseAsync(workspace);
        Directory.CreateDirectory(workspace.IndexDir);
        var docs = workspace.Docs().ToList();
        var fileIds = new Guid[docs.Count];
        var started = Stopwatch.GetTimestamp();
        var chunkCount = 0;

        // Same path as StoredFileProcessor: file row (queued -> processing), chunk, ReplaceChunksAsync, status ready.
        async Task IndexOneAsync(Doc doc, int index, Guid owner, CancellationToken cancellationToken)
        {
            await using var db = CreateDb(workspace);
            var files = new FileRepository(db);
            var bytes = Encoding.UTF8.GetBytes(doc.Text);
            var id = Guid.CreateVersion7();
            if (owner == Workspace.Owner) fileIds[index] = id;
            await files.CreateAsync(new StoredFile(id, owner, $"{owner:D}/{id:D}", doc.Name, "text/markdown", bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), DateTimeOffset.UtcNow, "queued"), cancellationToken);
            await files.SetProcessingStatusAsync(id, owner, "processing", cancellationToken);
            var chunks = Chunkers.Split(chunker, doc.Text, length, overlap);
            await new FileContentRepository(db).ReplaceChunksAsync(id, owner,
                chunks.Select((chunk, i) => new FileContentChunk(id, owner, i, chunk.Content, chunk.Start, chunk.End)).ToList(),
                cancellationToken);
            await files.SetProcessingStatusAsync(id, owner, "ready", cancellationToken);
            if (owner == Workspace.Owner) Interlocked.Add(ref chunkCount, chunks.Count);
        }

        await Parallel.ForEachAsync(docs.Select((doc, index) => (doc, index)),
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (item, token) => await IndexOneAsync(item.doc, item.index, Workspace.Owner, token));
        // A second owner holds copies of some documents: any hit from it is an owner-scope leak.
        if (foreignEvery > 0)
            await Parallel.ForEachAsync(docs.Select((doc, index) => (doc, index)).Where(x => x.index % foreignEvery == 0),
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (item, token) => await IndexOneAsync(item.doc, item.index, Workspace.ForeignOwner, token));

        await using (var db = CreateDb(workspace))
            await db.Database.ExecuteSqlRawAsync("ANALYZE files; ANALYZE file_content_chunks");
        Console.WriteLine($"Indexed {docs.Count} documents into {chunkCount} chunks in {Stopwatch.GetElapsedTime(started).TotalSeconds:0}s ({workspace.DbName}).");
        await File.WriteAllTextAsync(Path.Combine(workspace.IndexDir, "files.json"), JsonSerializer.Serialize(fileIds));
        await ExportChunksAsync(workspace, docs, fileIds);
    }

    private static async Task ExportChunksAsync(Workspace workspace, IReadOnlyList<Doc> docs, Guid[] fileIds)
    {
        var docByFile = fileIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        await using var source = workspace.DataSource();
        await using var command = source.CreateCommand("""
            SELECT "Id", file_id, chunk_index, start_offset, end_offset, content FROM file_content_chunks
            WHERE owner_id = $1 ORDER BY file_id, chunk_index
            """);
        command.Parameters.AddWithValue(Workspace.Owner);
        await using var reader = await command.ExecuteReaderAsync();
        await using var output = new StreamWriter(workspace.ChunksPath, false, new UTF8Encoding(false));
        var rows = 0;
        while (await reader.ReadAsync())
        {
            var row = new ChunkRow(reader.GetGuid(0), docByFile[reader.GetGuid(1)], reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetString(5));
            await output.WriteLineAsync(JsonSerializer.Serialize(row, Json.Web));
            rows++;
        }
        Console.WriteLine($"Exported {rows} chunks to {workspace.ChunksPath}.");
    }

    /// <summary>Overwrites every chunk embedding from a raw float32 matrix (rows in chunks.jsonl order), zero-padded to 1536.</summary>
    public static async Task LoadEmbeddingsAsync(Workspace workspace, string model)
    {
        var meta = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(workspace.IndexDir, $"emb-{model}.json"))).RootElement;
        var dim = meta.GetProperty("dim").GetInt32();
        // Re-indexing rewrites chunks.jsonl with a new row order; vectors made for an older export would silently misalign.
        if (meta.TryGetProperty("chunks_sha", out var expected) &&
            !string.Equals(expected.GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workspace.ChunksPath))), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"emb-{model} was made for a different chunks.jsonl; embed again after re-indexing.");
        var ids = Json.ReadLines<ChunkRow>(workspace.ChunksPath).Select(row => row.Id).ToArray();
        var raw = await File.ReadAllBytesAsync(Path.Combine(workspace.IndexDir, $"emb-{model}.f32"));
        if (raw.Length != ids.Length * dim * 4)
            throw new InvalidOperationException($"Embedding file holds {raw.Length / 4 / dim} vectors for {ids.Length} chunks.");
        var started = Stopwatch.GetTimestamp();
        await using var source = workspace.DataSource();
        await using var connection = await source.OpenConnectionAsync();
        await Execute(connection, "SET maintenance_work_mem = '1GB'");
        var indexNames = new List<string>();
        await using (var find = new NpgsqlCommand("SELECT indexname FROM pg_indexes WHERE tablename = 'file_content_chunks' AND indexdef LIKE '%hnsw%'", connection))
        await using (var reader = await find.ExecuteReaderAsync())
            while (await reader.ReadAsync()) indexNames.Add(reader.GetString(0));
        foreach (var name in indexNames) await Execute(connection, $"DROP INDEX \"{name}\"");
        await Execute(connection, "UPDATE file_content_chunks SET embedding = NULL WHERE embedding IS NOT NULL");
        await Execute(connection, "CREATE TEMP TABLE staged (id uuid PRIMARY KEY, embedding vector(1536))");
        await using (var import = await connection.BeginBinaryImportAsync("COPY staged (id, embedding) FROM STDIN (FORMAT BINARY)"))
        {
            var values = new float[1536];
            for (var row = 0; row < ids.Length; row++)
            {
                Array.Clear(values);
                Buffer.BlockCopy(raw, row * dim * 4, values, 0, dim * 4);
                await import.StartRowAsync();
                await import.WriteAsync(ids[row], NpgsqlTypes.NpgsqlDbType.Uuid);
                await import.WriteAsync(new Vector(values));
            }
            await import.CompleteAsync();
        }
        await Execute(connection, "UPDATE file_content_chunks c SET embedding = s.embedding FROM staged s WHERE c.\"Id\" = s.id");
        await Execute(connection, "CREATE INDEX \"IX_file_content_chunks_embedding\" ON file_content_chunks USING hnsw (embedding vector_cosine_ops)");
        await Execute(connection, "CREATE TABLE IF NOT EXISTS bench_meta (key text PRIMARY KEY, value text)");
        await Execute(connection, $"INSERT INTO bench_meta VALUES ('embedding_model', '{model}') ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value");
        await Execute(connection, "VACUUM ANALYZE file_content_chunks");
        Console.WriteLine($"Loaded {ids.Length} {model} embeddings ({dim} dims) and rebuilt the HNSW index in {Stopwatch.GetElapsedTime(started).TotalSeconds:0}s.");
    }

    /// <summary>
    /// Adds what the lexical variants need beyond production's search_text ('simple' config): a stemmed English and Dutch
    /// tsvector column with its own GIN index, and a document-frequency table per config (for BM25).
    /// </summary>
    public static async Task PrepareLexicalAsync(Workspace workspace)
    {
        await using var source = workspace.DataSource();
        await using var connection = await source.OpenConnectionAsync();
        await Execute(connection, "SET maintenance_work_mem = '512MB'");
        await Execute(connection, "CREATE EXTENSION IF NOT EXISTS pg_textsearch");
        foreach (var (config, column) in new[] { ("simple", "search_text"), ("english", "search_en"), ("dutch", "search_nl") })
        {
            if (column != "search_text")
            {
                var has = await Scalar(connection, $"SELECT count(*) FROM information_schema.columns WHERE table_name = 'file_content_chunks' AND column_name = '{column}'");
                if (has == 0)
                {
                    await Execute(connection, $"ALTER TABLE file_content_chunks ADD COLUMN {column} tsvector GENERATED ALWAYS AS (to_tsvector('{config}'::regconfig, content)) STORED");
                    await Execute(connection, $"CREATE INDEX ix_chunks_{column} ON file_content_chunks USING gin ({column})");
                }
            }
            await Execute(connection, $"DROP TABLE IF EXISTS bench_df_{config}");
            await Execute(connection, $"""
                CREATE TABLE bench_df_{config} AS
                SELECT word, ndoc FROM ts_stat($$SELECT {column} FROM file_content_chunks WHERE owner_id = '{Workspace.Owner}'$$)
                """);
            await Execute(connection, $"ALTER TABLE bench_df_{config} ADD PRIMARY KEY (word)");
        }
        await Execute(connection, "VACUUM ANALYZE file_content_chunks");
        Console.WriteLine("Prepared stemmed columns and document-frequency tables.");
    }

    /// <summary>pg_textsearch allows one bm25 index per column, so switch it to the requested text config when needed.</summary>
    public static async Task EnsureBm25Async(NpgsqlDataSource source, string config)
    {
        await using var connection = await source.OpenConnectionAsync();
        var bm25Indexes = await Scalar(connection, "SELECT count(*) FROM pg_indexes WHERE indexdef LIKE '%USING bm25%'");
        var wanted = await Scalar(connection, $"SELECT count(*) FROM pg_indexes WHERE indexname = 'ix_bm25_{config}'");
        if (bm25Indexes == 1 && wanted == 1) return;
        await Execute(connection, "SET maintenance_work_mem = '512MB'");
        foreach (var other in new[] { "simple", "english", "dutch" })
            await Execute(connection, $"DROP INDEX IF EXISTS ix_bm25_{other}");
        await Execute(connection, $"CREATE INDEX ix_bm25_{config} ON file_content_chunks USING bm25(content) WITH (text_config='{config}')");
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
