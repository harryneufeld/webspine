using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Content.Sqlite;

public sealed partial class SqliteContentSource
{
    public async Task<CompositionMigrationResult> MigrateToCompositionAsync(string expectedRevision,
        CompositionDesign design, Func<ContentSnapshot, CompositionWebsite> convert,
        ImmutableArray<SectionIdentityMapping> identityMap, CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction(deferred: false);
        var head = await Head(connection, transaction, ct) ?? throw new SiteNotInitializedException();
        if (head.Revision != expectedRevision) throw new RevisionConflictException();
        if (head.Version == 2)
        {
            var existing = await CompositionRevision(connection, transaction, head.Revision, ct) ?? throw new ContentValidationException("Missing composition head.");
            CompositionContract.Validate(existing, design, compositionRegistry);
            await CaptureAssets(connection, transaction, existing.Website.Assets, ct);
            transaction.Commit();
            return new(null, head.Revision, true, []);
        }
        var legacy = await LegacyRevision(connection, transaction, head.Revision, ct) ?? throw new ContentValidationException("Missing legacy revision.");
        var website = convert(legacy);
        if (website.Id != legacy.Website.Id || !website.Pages.Select(p => (p.Id, p.Path, p.Title, p.Description))
            .SequenceEqual(legacy.Website.Pages.Select(p => (p.Id, p.Path, p.Title, p.Description))) ||
            website.Title != legacy.Website.Title || website.Language != legacy.Website.Language || !website.Assets.SequenceEqual(legacy.Website.Assets))
            throw new ContentValidationException("Migration must preserve site/page identity, metadata and assets.");
        var snapshot = new CompositionSnapshot(2, Identity, Guid.NewGuid().ToString("N"), website);
        CompositionContract.Validate(snapshot, design, compositionRegistry);
        var expectedSections = legacy.Website.Pages.SelectMany(p => p.Sections.Select(s => (p.Id, s.Id))).ToHashSet();
        if (identityMap.IsDefault || identityMap.Length != expectedSections.Count ||
            identityMap.Select(m => m.BlockId).Distinct(StringComparer.Ordinal).Count() != identityMap.Length ||
            !identityMap.Select(m => (m.PageId, m.SectionId)).ToHashSet().SetEquals(expectedSections) ||
            identityMap.Any(m => !website.Blocks.Any(b => b.Id == m.BlockId && b.Owner == new BlockOwner(OwnerKind.Page, m.PageId)) ||
                !website.Pages.Single(p => p.Id == m.PageId).Regions.SelectMany(r => r.Placements)
                    .Any(p => p.Id == m.PlacementId && p.Kind == TargetKind.Block && p.TargetId == m.BlockId)))
            throw new ContentValidationException("Invalid section identity mapping.");
        var originalBlocks = LegacyCompositionMapping.Blocks(legacy).ToDictionary(b => b.Id, StringComparer.Ordinal);
        var originalMappings = LegacyCompositionMapping.Identities(legacy).ToDictionary(m => (m.PageId, m.SectionId));
        foreach (var mapping in identityMap)
        {
            var expectedBlock = originalBlocks[originalMappings[(mapping.PageId, mapping.SectionId)].BlockId];
            var actual = website.Blocks.Single(b => b.Id == mapping.BlockId);
            if (actual.TypeId != expectedBlock.TypeId || actual.TypeVersion != expectedBlock.TypeVersion ||
                !JsonElement.DeepEquals(actual.Fields, expectedBlock.Fields)) throw new ContentValidationException("Migration changed an existing section's values/type.");
        }
        var mappedSections = identityMap.ToDictionary(m => (m.PageId, m.SectionId));
        foreach (var page in legacy.Website.Pages)
        {
            var expectedOrder = page.Sections.Select(s => mappedSections[(page.Id, s.Id)].BlockId).ToArray();
            var sectionIds = expectedOrder.ToHashSet(StringComparer.Ordinal);
            var actualOrder = website.Pages.Single(p => p.Id == page.Id).Regions.Single(r => r.Id == "main").Placements
                .Where(p => p.Kind == TargetKind.Block && sectionIds.Contains(p.TargetId)).Select(p => p.TargetId);
            if (!actualOrder.SequenceEqual(expectedOrder)) throw new ContentValidationException("Migration changed section ordering/placement.");
        }
        var assets = await CaptureAssets(connection, transaction, website.Assets, ct);
        // Rehearse a full build, including media/path/renderer failures, before writing the new head.
        new CompositionBuildPipeline(compositionRegistry).Build(new(snapshot, design, assets), ct);
        await InsertComposition(connection, transaction, snapshot, ct);
        using var transition = Command(connection, transaction, "INSERT INTO content_transitions(revision,from_revision,original_revision,operation,identity_map) VALUES($new,$old,$old,'migrate',$map)");
        transition.Parameters.AddWithValue("$new", snapshot.Revision); transition.Parameters.AddWithValue("$old", legacy.Revision);
        transition.Parameters.AddWithValue("$map", JsonSerializer.Serialize(identityMap, CompositionJson.Options));
        await transition.ExecuteNonQueryAsync(ct);
        await ReplaceHead(connection, transaction, legacy.Revision, snapshot.Revision, ct);
        transaction.Commit();
        return new(legacy.Revision, snapshot.Revision, false, identityMap);
    }

    public async Task<ContentSnapshot> RestoreLegacyAsync(string expectedRevision, string originalRevision, CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction(deferred: false);
        var head = await Head(connection, transaction, ct) ?? throw new SiteNotInitializedException();
        if (head.Revision != expectedRevision) throw new RevisionConflictException();
        var original = await LegacyRevision(connection, transaction, originalRevision, ct)
            ?? throw new ContentValidationException("Recovery requires an existing v1 revision.");
        await CaptureAssets(connection, transaction, original.Website.Assets, ct);
        var restored = original with { Source = Identity, Revision = Guid.NewGuid().ToString("N") };
        await InsertRevision(connection, transaction, restored, ct);
        using var transition = Command(connection, transaction, "INSERT INTO content_transitions(revision,from_revision,original_revision,operation,identity_map) VALUES($new,$old,$original,'restore','[]')");
        transition.Parameters.AddWithValue("$new", restored.Revision); transition.Parameters.AddWithValue("$old", head.Revision);
        transition.Parameters.AddWithValue("$original", originalRevision);
        await transition.ExecuteNonQueryAsync(ct);
        await ReplaceHead(connection, transaction, head.Revision, restored.Revision, ct);
        transaction.Commit();
        return restored;
    }

    public async Task<ImmutableArray<SectionIdentityMapping>> MigrationMapAsync(string revision, CancellationToken ct = default)
    {
        await using var connection = await Open(ct);
        using var command = Command(connection, null, "SELECT identity_map FROM content_transitions WHERE revision=$revision AND operation='migrate'");
        command.Parameters.AddWithValue("$revision", revision);
        var json = await command.ExecuteScalarAsync(ct) as string;
        return json is null ? [] : JsonSerializer.Deserialize<ImmutableArray<SectionIdentityMapping>>(json, CompositionJson.Options);
    }
}

public static class LegacyCompositionMapping
{
    private static string Id(string prefix, string page, string section) => prefix + "-" +
        BuildPipeline.Hash(Encoding.UTF8.GetBytes(page + "/" + section))[..32];
    public static ImmutableArray<SectionIdentityMapping> Identities(ContentSnapshot legacy) => legacy.Website.Pages
        .SelectMany(p => p.Sections.Select(s => new SectionIdentityMapping(p.Id, s.Id, Id("block", p.Id, s.Id), Id("placement", p.Id, s.Id))))
        .ToImmutableArray();

    public static ImmutableArray<Block> Blocks(ContentSnapshot legacy) => legacy.Website.Pages.SelectMany(page => page.Sections.Select(section =>
    {
        object fields = section switch
        {
            TextSection text => new TextFields(text.Heading, text.Text),
            ImageSection image => new ImageFields(image.AssetId, image.AlternativeText),
            CtaSection cta => new CtaFields(cta.Heading, cta.Text, cta.Label, cta.Destination),
            CardsSection cards => new CardsFields(cards.Heading, cards.Items.Select(c => new CardFields(c.Title, c.Description, c.AssetId, c.Destination)).ToImmutableArray()),
            _ => throw new ContentValidationException("Unsupported v1 section migration.")
        };
        var type = section switch { TextSection => "text", ImageSection => "image", CtaSection => "cta", CardsSection => "cards", _ => throw new ContentValidationException("Unsupported section.") };
        return new Block(Id("block", page.Id, section.Id), new(OwnerKind.Page, page.Id), type, 1,
            JsonSerializer.SerializeToElement(fields, fields.GetType(), CompositionJson.Options), []);
    })).ToImmutableArray();
}
