using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Content.Sqlite;

public sealed record ContentHead(string Revision, int Version);
public sealed partial class SqliteContentSource
{
    // Typed operations are implemented by the application editor; commits remain trusted-host calls.
    public CompositionCapabilities CompositionCapabilities => new(2,
        compositionRegistry.Descriptors.Select(d => new SupportedBlockType(d.Id, d.Version)).ToImmutableArray(),
        Enum.GetValues<CompositionOperation>().ToImmutableArray(), true, true) { RecordSchemas = compositionRegistry.Records.Descriptors };

    public async Task<ContentHead?> HeadAsync(CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        return await Head(connection, null, ct);
    }
    private static async Task<ContentHead?> Head(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken ct)
    {
        using var command = Command(connection, transaction, "SELECT r.revision,r.contract_version FROM revisions r JOIN site s ON s.revision=r.revision WHERE s.id=1");
        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetString(0), reader.GetInt32(1)) : null;
    }
    public async Task<ContentSnapshot?> ReadLegacyRevisionAsync(string revision, CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        return await LegacyRevision(connection, null, revision, ct);
    }
    private static async Task<ContentSnapshot?> LegacyRevision(SqliteConnection connection, SqliteTransaction? transaction, string revision, CancellationToken ct)
    {
        using var command = Command(connection, transaction, "SELECT snapshot FROM revisions WHERE revision=$revision AND contract_version=1");
        command.Parameters.AddWithValue("$revision", revision);
        var json = await command.ExecuteScalarAsync(ct) as string;
        if (json is null) return null;
        var snapshot = JsonSerializer.Deserialize<ContentSnapshot>(json, Json) ?? throw new ContentValidationException("Invalid historical v1 content.");
        ContentContract.Validate(snapshot);
        return snapshot;
    }
    public async Task<CompositionSnapshot?> ReadCompositionRevisionAsync(string revision, CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        return await CompositionRevision(connection, null, revision, ct);
    }
    private static async Task<CompositionSnapshot?> CompositionRevision(SqliteConnection connection, SqliteTransaction? transaction, string revision, CancellationToken ct)
    {
        using var command = Command(connection, transaction, "SELECT snapshot FROM revisions WHERE revision=$revision AND contract_version=2");
        command.Parameters.AddWithValue("$revision", revision);
        var json = await command.ExecuteScalarAsync(ct) as string;
        if (json is null) return null;
        var snapshot = CompositionJson.Read(json);
        if (snapshot.ContractVersion != 2 || snapshot.Revision != revision) throw new ContentValidationException("Invalid stored composition identity/version.");
        return snapshot;
    }
    public async ValueTask<CompositionSnapshot> ReadCompositionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await Open(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var head = await Head(connection, transaction, cancellationToken) ?? throw new SiteNotInitializedException();
        if (head.Version != 2) throw new SourceOperationNotSupportedException("Legacy v1 authoring is no longer supported. Preserve this data directory and configure a separate empty Management:DataDirectory to create a new v2 site. Stored previews remain available.");
        var snapshot = await CompositionRevision(connection, transaction, head.Revision, cancellationToken)
            ?? throw new ContentValidationException("Composition head is missing.");
        transaction.Commit();
        return snapshot;
    }
    public async ValueTask<CapturedComposition> CaptureCompositionAsync(CompositionDesign design, CancellationToken cancellationToken = default)
    {
        await using var connection = await Open(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var head = await Head(connection, transaction, cancellationToken) ?? throw new SiteNotInitializedException();
        if (head.Version != 2) throw new SourceOperationNotSupportedException("Legacy v1 authoring is no longer supported. Create a fresh v2 workspace in a separate data directory; stored previews remain available.");
        var snapshot = await CompositionRevision(connection, transaction, head.Revision, cancellationToken)
            ?? throw new ContentValidationException("Composition head is missing.");
        CompositionContract.Validate(snapshot, design, compositionRegistry);
        var assets = await CaptureAssets(connection, transaction, snapshot.Website.Assets, cancellationToken);
        transaction.Commit();
        return new(snapshot, design, assets);
    }
    public async Task<CompositionSnapshot> CreateCompositionAsync(CompositionWebsite website, CompositionDesign design,
        ImmutableDictionary<string, ImmutableArray<byte>> assets, CancellationToken cancellationToken = default)
    {
        var snapshot = new CompositionSnapshot(2, Identity, Guid.NewGuid().ToString("N"), website);
        CompositionContract.Validate(snapshot, design, compositionRegistry);
        await using var connection = await Open(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        if (await Head(connection, transaction, cancellationToken) is not null) throw new SiteAlreadyExistsException();
        await EnsureAssets(connection, transaction, website.Assets, assets, cancellationToken);
        await InsertComposition(connection, transaction, snapshot, cancellationToken);
        using var create = Command(connection, transaction, "INSERT INTO site(id,revision) VALUES(1,$revision)");
        create.Parameters.AddWithValue("$revision", snapshot.Revision);
        await create.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
        return snapshot;
    }
    public async Task<CompositionSnapshot> CommitCompositionAsync(string expectedRevision, CompositionWebsite proposed, CompositionDesign design,
        ImmutableDictionary<string, ImmutableArray<byte>>? newAssets = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await Open(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        var head = await Head(connection, transaction, cancellationToken) ?? throw new SiteNotInitializedException();
        if (head.Revision != expectedRevision) throw new RevisionConflictException();
        if (head.Version != 2) throw new SourceOperationNotSupportedException("Composition commits require a v2 site.");
        var current = await CompositionRevision(connection, transaction, head.Revision, cancellationToken)
            ?? throw new ContentValidationException("Composition head is missing.");
        if (current.Website.Id != proposed.Id) throw new ContentValidationException("A commit cannot replace the website identity.");
        var snapshot = new CompositionSnapshot(2, Identity, Guid.NewGuid().ToString("N"), proposed);
        CompositionContract.Validate(snapshot, design, compositionRegistry);
        // Reject deletion even if callers also remove references: explicit detach/remove precedes a later delete.
        var removedShared = current.Website.SharedBlocks.Select(s => s.Id).Except(proposed.SharedBlocks.Select(s => s.Id)).ToHashSet(StringComparer.Ordinal);
        if (AllPlacements(current.Website).Any(p => p.Kind == TargetKind.Shared && removedShared.Contains(p.TargetId)))
            throw new ContentValidationException("Referenced Shared Blocks cannot be deleted.");
        foreach (var record in current.Website.Records)
        {
            var next = proposed.Records.FirstOrDefault(r => r.Id == record.Id);
            if (next is null && PatternContract.IsReferenced(current.Website, compositionRegistry, record.Id))
                throw new ContentValidationException("Referenced Records cannot be deleted; remove their references in an earlier conditional write.");
            if (next is not null && next.Revision == record.Revision && (next.SchemaId != record.SchemaId || next.SchemaVersion != record.SchemaVersion ||
                next.Fields.GetRawText() != record.Fields.GetRawText()))
                throw new ContentValidationException("Changed Record values require a new Record revision.");
        }
        await EnsureAssets(connection, transaction, proposed.Assets, newAssets ?? ImmutableDictionary<string, ImmutableArray<byte>>.Empty, cancellationToken);
        await InsertComposition(connection, transaction, snapshot, cancellationToken);
        await ReplaceHead(connection, transaction, expectedRevision, snapshot.Revision, cancellationToken);
        transaction.Commit();
        return snapshot;
    }
    private static IEnumerable<Placement> AllPlacements(CompositionWebsite site) =>
        site.Pages.SelectMany(p => p.Regions).SelectMany(r => r.Placements).Concat(site.Blocks.SelectMany(b => b.Children));

    private static async Task EnsureAssets(SqliteConnection connection, SqliteTransaction transaction, ImmutableArray<AssetContent> declared,
        ImmutableDictionary<string, ImmutableArray<byte>> supplied, CancellationToken ct)
    {
        if (supplied.Keys.Any(file => !declared.Any(a => a.File == file))) throw new ContentValidationException("Undeclared media supplied.");
        foreach (var asset in declared)
        {
            using var read = Command(connection, transaction, "SELECT bytes FROM assets WHERE file=$file");
            read.Parameters.AddWithValue("$file", asset.File);
            var existing = await read.ExecuteScalarAsync(ct) as byte[];
            if (supplied.TryGetValue(asset.File, out var bytes))
            {
                if (bytes.IsDefaultOrEmpty) throw new ContentValidationException("Empty media is invalid.");
                if (existing is not null && !existing.AsSpan().SequenceEqual(bytes.AsSpan()))
                    throw new ContentValidationException("Stored media paths are immutable; use a new path for replacement.");
                if (existing is null)
                {
                    using var insert = Command(connection, transaction, "INSERT INTO assets(file,bytes) VALUES($file,$bytes)");
                    insert.Parameters.AddWithValue("$file", asset.File); insert.Parameters.AddWithValue("$bytes", bytes.ToArray());
                    await insert.ExecuteNonQueryAsync(ct);
                }
            }
            else if (existing is null) throw new ContentValidationException("Referenced media is missing.");
        }
    }
    private static async Task<ImmutableDictionary<string, ImmutableArray<byte>>> CaptureAssets(SqliteConnection connection,
        SqliteTransaction transaction, ImmutableArray<AssetContent> declared, CancellationToken ct)
    {
        var result = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        foreach (var asset in declared)
        {
            using var command = Command(connection, transaction, "SELECT bytes FROM assets WHERE file=$file");
            command.Parameters.AddWithValue("$file", asset.File);
            var bytes = await command.ExecuteScalarAsync(ct) as byte[] ?? throw new ContentValidationException("Stored media is missing.");
            if (bytes.Length == 0) throw new ContentValidationException("Stored media is empty.");
            result.Add(asset.File, bytes.ToImmutableArray());
        }
        return result.ToImmutable();
    }
    private static async Task InsertComposition(SqliteConnection connection, SqliteTransaction transaction, CompositionSnapshot snapshot, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(snapshot, CompositionJson.Options);
        CompositionJson.Read(json); // Enforce the reader's bounds before persisting a revision it could not reopen.
        using var command = Command(connection, transaction, "INSERT INTO revisions(revision,created_utc,snapshot,contract_version) VALUES($revision,$created,$snapshot,2)");
        command.Parameters.AddWithValue("$revision", snapshot.Revision); command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$snapshot", json);
        await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task ReplaceHead(SqliteConnection connection, SqliteTransaction transaction, string expected, string replacement, CancellationToken ct)
    {
        using var command = Command(connection, transaction, "UPDATE site SET revision=$replacement WHERE id=1 AND revision=$expected");
        command.Parameters.AddWithValue("$replacement", replacement); command.Parameters.AddWithValue("$expected", expected);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new RevisionConflictException();
    }
}
