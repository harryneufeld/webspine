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
public sealed record DraftRevision(string Revision, string CreatedUtc);

public sealed class SqliteContentSource : IContentSource
{
    private readonly string connectionString;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public SourceIdentity Identity { get; } = new("builtin", "sqlite");
    public SourceCapabilities Capabilities { get; } = new(true, true, false);
    public SqliteContentSource(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = false, DefaultTimeout = 10 }.ToString();
    }
    public async Task InitializeSchemaAsync(CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var version = Command(connection, transaction, "PRAGMA user_version");
        var current = Convert.ToInt32(await version.ExecuteScalarAsync(ct));
        if (current > 1) throw new InvalidOperationException("The content database requires a newer version of webspine.");
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
        transaction.Commit();
    }
    public async Task<ContentSnapshot?> TryReadAsync(CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        return await Read(connection, null, ct);
    }
    public async ValueTask<ContentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        => await TryReadAsync(cancellationToken) ?? throw new SiteNotInitializedException();
    public async Task<ContentSnapshot> CreateAsync(WebsiteContent website, IReadOnlyDictionary<string, ImmutableArray<byte>> assets, CancellationToken ct = default)
    {
        var snapshot = new ContentSnapshot(Identity, Guid.NewGuid().ToString("N"), website);
        ContentContract.Validate(snapshot);
        foreach (var asset in website.Assets)
            if (!assets.ContainsKey(asset.File)) throw new ContentValidationException("A required image is missing.");
        await using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction(deferred: false);
        if (await Read(connection, transaction, ct) is not null) throw new SiteAlreadyExistsException();
        await InsertRevision(connection, transaction, snapshot, ct);
        using var create = Command(connection, transaction, "INSERT INTO site(id,revision) VALUES(1,$revision)");
        create.Parameters.AddWithValue("$revision", snapshot.Revision);
        await create.ExecuteNonQueryAsync(ct);
        foreach (var asset in website.Assets)
        {
            using var insert = Command(connection, transaction, "INSERT INTO assets(file,bytes) VALUES($file,$bytes)");
            insert.Parameters.AddWithValue("$file", asset.File);
            insert.Parameters.AddWithValue("$bytes", assets[asset.File].ToArray());
            await insert.ExecuteNonQueryAsync(ct);
        }
        transaction.Commit();
        return snapshot;
    }
    public async Task<ContentSnapshot> EditPageAsync(string expected, string pageId, string title, string description, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
        => await Change(expected, snapshot =>
        {
            var page = snapshot.Website.Pages.FirstOrDefault(p => p.Id == pageId) ?? throw new ContentValidationException("Page does not exist.");
            var allowed = page.Sections.SelectMany(s => ContentFields.Describe(s)).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
            if (fields.Keys.Any(key => !allowed.Contains(key))) throw new ContentValidationException("An unapproved field was submitted.");
            var edited = page with { Title = title, Description = description, Sections = page.Sections.Select(s => ContentFields.Apply(s, fields)).ToImmutableArray() };
            return snapshot.Website with { Pages = snapshot.Website.Pages.Replace(page, edited) };
        }, ct);
    public async Task<ContentSnapshot> AddPageAsync(string expected, string title, string path, string description, CancellationToken ct = default)
        => await Change(expected, snapshot => snapshot.Website with { Pages = snapshot.Website.Pages.Add(new("page-" + Guid.NewGuid().ToString("N"), path, title, description, [new TextSection("introduction", "Tell your story", "Add your page content here.")])) }, ct);
    public async ValueTask<ContentSnapshot> UpdateDraftAsync(DraftChange change, CancellationToken cancellationToken = default)
        => await Change(change.ExpectedRevision, snapshot =>
        {
            var page = snapshot.Website.Pages.FirstOrDefault(p => p.Id == change.PageId) ?? throw new ContentValidationException("Page does not exist.");
            var section = page.Sections.FirstOrDefault(s => s.Id == change.SectionId) ?? throw new ContentValidationException("Section does not exist.");
            var key = section.Id + "." + change.Field;
            if (!ContentFields.Describe(section).Any(f => f.Key == key)) throw new ContentValidationException("An unapproved field was submitted.");
            var updated = ContentFields.Apply(section, new Dictionary<string, string> { [key] = change.Value });
            return snapshot.Website with { Pages = snapshot.Website.Pages.Replace(page, page with { Sections = page.Sections.Replace(section, updated) }) };
        }, cancellationToken);
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
        using var command = Command(connection, null, "SELECT revision,created_utc FROM revisions ORDER BY rowid DESC");
        using var reader = await command.ExecuteReaderAsync(ct);
        var rows = ImmutableArray.CreateBuilder<DraftRevision>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetString(0), reader.GetString(1)));
        return rows.ToImmutable();
    }
    public async Task SavePreviewAsync(string id, BuiltArtifact artifact, CancellationToken ct = default)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ContentValidationException("Invalid preview identifier.");
        await using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction(deferred: false);
        var head = await Read(connection, transaction, ct) ?? throw new SiteNotInitializedException();
        if (head.Revision != artifact.SourceRevision || artifact.Source != Identity) throw new RevisionConflictException();
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
    private async Task<ContentSnapshot> Change(string expected, Func<ContentSnapshot, WebsiteContent> edit, CancellationToken ct)
    {
        await using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction(deferred: false);
        var snapshot = await Read(connection, transaction, ct) ?? throw new SiteNotInitializedException();
        if (snapshot.Revision != expected) throw new RevisionConflictException();
        var changed = new ContentSnapshot(Identity, Guid.NewGuid().ToString("N"), edit(snapshot));
        ContentContract.Validate(changed);
        await InsertRevision(connection, transaction, changed, ct);
        using var update = Command(connection, transaction, "UPDATE site SET revision=$new WHERE id=1 AND revision=$expected");
        update.Parameters.AddWithValue("$new", changed.Revision);
        update.Parameters.AddWithValue("$expected", expected);
        if (await update.ExecuteNonQueryAsync(ct) != 1) throw new RevisionConflictException();
        transaction.Commit();
        return changed;
    }
    private static async Task InsertRevision(SqliteConnection connection, SqliteTransaction transaction, ContentSnapshot snapshot, CancellationToken ct)
    {
        using var command = Command(connection, transaction, "INSERT INTO revisions(revision,created_utc,snapshot) VALUES($revision,$created,$snapshot)");
        command.Parameters.AddWithValue("$revision", snapshot.Revision);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(snapshot, Json));
        await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task<ContentSnapshot?> Read(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken ct)
    {
        using var command = Command(connection, transaction, "SELECT snapshot FROM revisions JOIN site ON site.revision=revisions.revision WHERE site.id=1");
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
