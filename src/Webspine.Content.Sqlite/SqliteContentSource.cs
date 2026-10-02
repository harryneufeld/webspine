using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Webspine.Core;

namespace Webspine.Content.Sqlite;

public sealed class RevisionConflictException() : Exception("This draft changed since you opened it. Your changes were not saved.");
public sealed class SiteAlreadyExistsException() : Exception("A site already exists. Setup cannot replace it.");
public sealed class SiteNotInitializedException() : Exception("Create a site before editing content.");
public sealed record StoredContent(ContentSnapshot Snapshot, ImmutableDictionary<string, ImmutableArray<byte>> Assets);
public sealed record DraftRevision(string Revision, string CreatedUtc, int ContractVersion = 1);
public sealed class CompositionSiteException() : Exception("This site uses composition v2. The v1 editor cannot edit it; use the composition board or v2 API.");

public sealed partial class SqliteContentSource : IContentSource, Webspine.Core.Composition.ICompositionDraftPersistence
{
    private readonly string connectionString;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public SourceIdentity Identity { get; } = new("builtin", "sqlite");
    public SourceCapabilities Capabilities { get; } = new(true, false, false);
    private readonly Webspine.Core.Composition.BlockRegistry compositionRegistry;
    public SqliteContentSource(string databasePath, Webspine.Core.Composition.BlockRegistry? compositionRegistry = null)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = false, DefaultTimeout = 10 }.ToString();
        this.compositionRegistry = compositionRegistry ?? new(Webspine.Core.Composition.StandardBlocks.Registrations);
    }
    public async Task InitializeSchemaAsync(CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var version = Command(connection, transaction, "PRAGMA user_version");
        var current = Convert.ToInt32(await version.ExecuteScalarAsync(ct));
        if (current > 2) throw new InvalidOperationException("The content database requires a newer version of webspine.");
        if (current == 0)
        {
            using var schema = Command(connection, transaction, """
                CREATE TABLE revisions (revision TEXT PRIMARY KEY, created_utc TEXT NOT NULL, snapshot TEXT NOT NULL);
                CREATE TABLE site (id INTEGER PRIMARY KEY CHECK(id = 1), revision TEXT NOT NULL REFERENCES revisions(revision));
                CREATE TABLE assets (file TEXT PRIMARY KEY, bytes BLOB NOT NULL);
                CREATE TABLE previews (id TEXT PRIMARY KEY, source_revision TEXT NOT NULL REFERENCES revisions(revision), artifact TEXT NOT NULL);
                PRAGMA user_version = 1;
                """);
            await schema.ExecuteNonQueryAsync(ct);
        }
        if (current < 2)
        {
            using var upgrade = Command(connection, transaction, """
                ALTER TABLE revisions ADD COLUMN contract_version INTEGER NOT NULL DEFAULT 1 CHECK(contract_version IN (1,2));
                PRAGMA user_version = 2;
                """);
            await upgrade.ExecuteNonQueryAsync(ct);
        }
        transaction.Commit();
    }
    public async Task<ContentSnapshot?> TryReadAsync(CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        return await Read(connection, null, ct);
    }
    public async ValueTask<ContentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        => await TryReadAsync(cancellationToken) ?? throw new SiteNotInitializedException();
    public ValueTask<ContentSnapshot> UpdateDraftAsync(DraftChange change, CancellationToken cancellationToken = default)
        => ValueTask.FromException<ContentSnapshot>(new SourceOperationNotSupportedException("Legacy draft editing has been removed. Use composition v2."));
    public async Task<StoredContent> CaptureAsync(CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshot = await Read(connection, transaction, ct) ?? throw new SiteNotInitializedException();
        var assets = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        foreach (var asset in snapshot.Website.Assets)
        {
            using var command = Command(connection, transaction, "SELECT bytes FROM assets WHERE file=$file");
            command.Parameters.AddWithValue("$file", asset.File);
            var bytes = await command.ExecuteScalarAsync(ct) as byte[] ?? throw new ContentValidationException("Stored image is missing.");
            assets.Add(asset.File, bytes.ToImmutableArray());
        }
        transaction.Commit();
        return new(snapshot, assets.ToImmutable());
    }
    public async Task<ImmutableArray<DraftRevision>> HistoryAsync(CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        using var command = Command(connection, null, "SELECT revision,created_utc,contract_version FROM revisions ORDER BY rowid DESC");
        using var reader = await command.ExecuteReaderAsync(ct);
        var rows = ImmutableArray.CreateBuilder<DraftRevision>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        return rows.ToImmutable();
    }
    public async Task SavePreviewAsync(string id, BuiltArtifact artifact, CancellationToken ct = default)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ContentValidationException("Invalid preview identifier.");
        await using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction(deferred: false);
        var head = await Head(connection, transaction, ct) ?? throw new SiteNotInitializedException();
        if (head.Revision != artifact.SourceRevision || artifact.Source != Identity || head.Version != artifact.ContractVersion) throw new RevisionConflictException();
        using var command = Command(connection, transaction, "INSERT INTO previews(id,source_revision,artifact) VALUES($id,$revision,$artifact)");
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$revision", artifact.SourceRevision);
        command.Parameters.AddWithValue("$artifact", JsonSerializer.Serialize(artifact, Json));
        await command.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }
    public async Task<BuiltArtifact?> ReadPreviewAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return null;
        await using var connection = await Open(ct);
        using var command = Command(connection, null, "SELECT artifact FROM previews WHERE id=$id");
        command.Parameters.AddWithValue("$id", id);
        var json = await command.ExecuteScalarAsync(ct) as string;
        return json is null ? null : JsonSerializer.Deserialize<BuiltArtifact>(json, Json);
    }
    private static async Task<ContentSnapshot?> Read(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken ct)
    {
        var head = await Head(connection, transaction, ct);
        if (head is null) return null;
        if (head.Version != 1) throw new CompositionSiteException();
        using var command = Command(connection, transaction, "SELECT snapshot FROM revisions WHERE revision=$revision");
        command.Parameters.AddWithValue("$revision", head.Revision);
        var json = await command.ExecuteScalarAsync(ct) as string;
        if (json is null) return null;
        var snapshot = JsonSerializer.Deserialize<ContentSnapshot>(json, Json) ?? throw new ContentValidationException("Stored content is invalid.");
        ContentContract.Validate(snapshot);
        return snapshot;
    }
    private async Task<SqliteConnection> Open(CancellationToken ct)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON";
            await command.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; return command;
    }
}
