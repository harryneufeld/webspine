using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Demo;

static class CompositionStoreChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Fails<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static async Task<object?> Sql(string path, string sql, params (string, object)[] parameters)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }
    private static CompositionWebsite Convert(ContentSnapshot legacy)
    {
        var map = LegacyCompositionMapping.Identities(legacy).ToDictionary(m => (m.PageId, m.SectionId));
        return DemoComposition.Convert(legacy, LegacyCompositionMapping.Blocks(legacy), (page, section) =>
        { var item = map[(page, section)]; return (item.BlockId, item.PlacementId); });
    }

    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-composition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "webspine.db");
            var registry = DemoComposition.Registry();
            var design = await DemoComposition.DesignAsync();
            var store = new SqliteContentSource(path, registry);
            await store.InitializeSchemaAsync();
            var fixture = await new DemoContentSource(await File.ReadAllTextAsync(Path.Combine(DemoSite.FixtureDirectory, "site.json"))).ReadAsync();
            var assets = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(StringComparer.Ordinal);
            foreach (var asset in fixture.Website.Assets)
                assets.Add(asset.File, (await File.ReadAllBytesAsync(Path.Combine(DemoSite.FixtureDirectory, asset.File))).ToImmutableArray());
            var legacy = await store.CreateAsync(fixture.Website, assets.ToImmutable());
            var previewId = Guid.NewGuid().ToString("N");
            var preview = await DemoSite.BuildSnapshotAsync(legacy, assets.ToImmutable(), "/manage/preview/" + previewId);
            await store.SavePreviewAsync(previewId, preview);
            var originalJson = (string)(await Sql(path, "SELECT snapshot FROM revisions WHERE revision=$revision", ("$revision", legacy.Revision)))!;
            var originalPreviewJson = (string)(await Sql(path, "SELECT artifact FROM previews WHERE id=$id", ("$id", previewId)))!;
            // Reproduce the actual previously shipped v1 schema rather than testing only a new database.
            await Sql(path, "DROP TABLE content_transitions; ALTER TABLE revisions DROP COLUMN contract_version; PRAGMA user_version=1;");
            await store.InitializeSchemaAsync(); await store.InitializeSchemaAsync();
            Require(System.Convert.ToInt32(await Sql(path, "PRAGMA user_version")) == 2 && (await store.ReadAsync()).Revision == legacy.Revision,
                "Schema upgrade changed the authoritative content.");
            Require((string)(await Sql(path, "SELECT snapshot FROM revisions WHERE revision=$revision", ("$revision", legacy.Revision)))! == originalJson,
                "Schema upgrade rewrote history.");
            Console.WriteLine("PASS: Real v1 database metadata upgrades idempotently without changing content/history/previews.");

            var map = LegacyCompositionMapping.Identities(legacy);
            var incompatible = design with { Layout = design.Layout with { Id = "different" } };
            await Fails<ContentValidationException>(() => store.MigrateToCompositionAsync(legacy.Revision, incompatible, Convert, map));
            await Fails<ContentValidationException>(() => store.MigrateToCompositionAsync(legacy.Revision, design, current =>
            {
                var changed = Convert(current);
                var block = changed.Blocks.First(b => b.TypeId == "text");
                return changed with { Blocks = changed.Blocks.Replace(block, block with { Fields = JsonSerializer.SerializeToElement(new TextFields("Lost original value", "Body"), CompositionJson.Options) }) };
            }, map));
            await Fails<ContentValidationException>(() => store.MigrateToCompositionAsync(legacy.Revision, design, current =>
            {
                var changed = Convert(current);
                var page = changed.Pages[0]; var main = page.Regions.Single(r => r.Id == "main");
                return changed with { Pages = changed.Pages.SetItem(0, page with
                    { Regions = page.Regions.Replace(main, main with { Placements = main.Placements.Reverse().ToImmutableArray() }) }) };
            }, map));
            Require((await store.HeadAsync())!.Revision == legacy.Revision && (await store.HistoryAsync()).Length == 1, "Failed migration partially committed.");
            await Fails<RevisionConflictException>(() => store.MigrateToCompositionAsync("stale", design, Convert, map));
            Console.WriteLine("PASS: Invalid/lossy/stale migrations preserve the original head and leave no partial revision.");

            var migration = await store.MigrateToCompositionAsync(legacy.Revision, design, Convert, map);
            var migrated = await store.ReadCompositionAsync();
            Require(migration.Revision == migrated.Revision && migrated.Website.Pages.Select(p => (p.Id, p.Path, p.Title, p.Description))
                .SequenceEqual(legacy.Website.Pages.Select(p => (p.Id, p.Path, p.Title, p.Description))), "Migration lost page identity/content.");
            Require((await store.MigrationMapAsync(migrated.Revision)).SequenceEqual(map), "Identity mapping not persisted.");
            var repeated = await store.MigrateToCompositionAsync(migrated.Revision, design, Convert, []);
            Require(repeated.AlreadyComposition && repeated.Revision == migrated.Revision && (await store.HistoryAsync()).Length == 2, "Repeated migration changed content.");
            await Fails<CompositionSiteException>(async () => await store.UpdateDraftAsync(new(legacy.Revision, "home", "introduction", "heading", "Old form")));
            Require((await store.ReadLegacyRevisionAsync(legacy.Revision))!.Website.Title == legacy.Website.Title, "Legacy history unreadable.");
            var capture = await store.CaptureCompositionAsync(design);
            var built = new CompositionBuildPipeline(registry).Build(capture);
            var html = Encoding.UTF8.GetString(built.Files.Single(f => f.Path == "index.html").Bytes.AsSpan());
            Require(html.Contains("Main navigation") && html.Contains("/contact/") && html.Contains(legacy.Website.Title) && html.Contains("Draft preview"), "Shared shell lost during migration.");
            Require(capture.AssetFiles.Single().Value.SequenceEqual(assets.Single().Value), "Migration changed media.");
            Console.WriteLine("PASS: Explicit v1 migration preserves all fields/pages/media, captures shared shell and retains its identity map.");

            var reopened = new SqliteContentSource(path, DemoComposition.Registry());
            await reopened.InitializeSchemaAsync();
            var home = migrated.Website.Pages.Single(p => p.Id == "home");
            var main = home.Regions.Single(r => r.Id == "main");
            var oldPlacement = main.Placements[1];
            var group = new Block("nested-group", new(OwnerKind.Page, home.Id), "group", 1,
                JsonSerializer.SerializeToElement(new GroupFields("stack", "start", "medium", 1), CompositionJson.Options), [oldPlacement]);
            var changedHome = home with { Regions = home.Regions.Replace(main, main with { Placements = main.Placements.SetItem(1, new("nested-placement", TargetKind.Block, group.Id)) }) };
            var header = migrated.Website.Blocks.Single(b => b.TypeId == "site-header");
            var proposed = migrated.Website with { Blocks = migrated.Website.Blocks.Add(group).Replace(header, header with
                { Fields = JsonSerializer.SerializeToElement(new HeaderFields("Changed subtitle", migrated.Website.Pages.Select(p => p.Id).ToImmutableArray()), CompositionJson.Options) }),
                Pages = migrated.Website.Pages.Replace(home, changedHome) };
            var saved = await ((ICompositionDraftPersistence)store).CommitCompositionAsync(migrated.Revision, proposed, design);
            await Fails<RevisionConflictException>(() => reopened.CommitCompositionAsync(migrated.Revision, proposed, design));
            await Fails<ContentValidationException>(() => reopened.CommitCompositionAsync(saved.Revision, proposed with { Blocks = proposed.Blocks.Remove(group) }, design));
            Require((await reopened.ReadCompositionAsync()).Revision == saved.Revision, "Restart/invalid graph changed head.");
            Require((await new CompositionBuildPipeline(registry).BuildAsync(reopened, design)).Digest != built.Digest, "Adapter capture ignored committed graph.");
            var v2previewId = Guid.NewGuid().ToString("N");
            var v2preview = await new CompositionBuildPipeline(registry).BuildAsync(reopened, design);
            await reopened.SavePreviewAsync(v2previewId, v2preview);
            Require((await reopened.ReadPreviewAsync(v2previewId))!.Digest == v2preview.Digest, "v2 preview not retained.");
            Console.WriteLine("PASS: Atomic multi-object composition commits persist nesting/shared content, reject stale/invalid edits and reopen consistently.");

            async Task<bool> Compete(string title)
            {
                try { await reopened.CommitCompositionAsync(saved.Revision, proposed with { Title = title }, design); return true; }
                catch (RevisionConflictException) { return false; }
            }
            var winners = await Task.WhenAll(Task.Run(() => Compete("Writer one")), Task.Run(() => Compete("Writer two")));
            Require(winners.Count(v => v) == 1 && (await store.HistoryAsync()).Length == 4, "Concurrent composition writes both committed.");
            var current = await store.ReadCompositionAsync();
            await Fails<RevisionConflictException>(() => store.SavePreviewAsync(Guid.NewGuid().ToString("N"), v2preview));
            Console.WriteLine("PASS: Concurrent composition writers have one winner and stale preview saves are rejected.");

            var footer = current.Website.Blocks.Single(b => b.Id == "site-footer-root");
            var footerOwner = new BlockOwner(OwnerKind.Shared, "site-footer");
            var footerImage = new Block("footer-image", footerOwner, "image", 1,
                JsonSerializer.SerializeToElement(new ImageFields(current.Website.Assets.Single().Id, "Shared footer image"), CompositionJson.Options), []);
            var footerContent = footer with { Id = "footer-content" };
            var nestedFooter = new Block("footer-nested", footerOwner, "group", 1,
                JsonSerializer.SerializeToElement(new GroupFields("stack", "start", "small", 1), CompositionJson.Options),
                [new("footer-content-placement", TargetKind.Block, footerContent.Id)]);
            var footerGroup = footer with { TypeId = "group", Fields = JsonSerializer.SerializeToElement(new GroupFields("stack", "start", "medium", 1), CompositionJson.Options),
                Children = [new("footer-image-placement", TargetKind.Block, footerImage.Id), new("footer-nested-placement", TargetKind.Block, nestedFooter.Id)] };
            current = await store.CommitCompositionAsync(current.Revision, current.Website with
                { Blocks = current.Website.Blocks.Replace(footer, footerGroup).Add(footerImage).Add(footerContent).Add(nestedFooter) }, design);
            var detachedFooter = CompositionCopies.DetachShared(current, design, registry, DemoComposition.PlacementId("footer", "home"));
            var originalIds = current.Website.Blocks.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
            var copiedFooterBlocks = detachedFooter.Blocks.Where(b => !originalIds.Contains(b.Id)).ToArray();
            Require(copiedFooterBlocks.Length == 4 && copiedFooterBlocks.All(b => b.Owner == new BlockOwner(OwnerKind.Page, "home")) &&
                JsonElement.DeepEquals(copiedFooterBlocks.Single(b => b.TypeId == "image").Fields, footerImage.Fields), "Nested detach lost structure, ownership or image reference.");
            current = await store.CommitCompositionAsync(current.Revision, detachedFooter, design);
            Console.WriteLine("PASS: Nested shared Groups detach into independent subtrees with new IDs and unchanged image references.");

            await Fails<ContentValidationException>(() => Task.FromResult(CompositionCopies.DeleteUnreferencedShared(current, design, registry, "site-header")));
            var detachedSite = CompositionCopies.DetachShared(current, design, registry, DemoComposition.PlacementId("header", "home"));
            var detached = await store.CommitCompositionAsync(current.Revision, detachedSite, design);
            var reference = detached.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "header").Placements.Single();
            Require(reference.Kind == TargetKind.Block && reference.TargetId != "site-header-root" && detached.Website.SharedBlocks.Any(s => s.Id == "site-header"), "Detach did not copy with new identity.");
            var detachedHeader = detached.Website.Blocks.Single(b => b.Id == reference.TargetId);
            Require(detachedHeader.Owner == new BlockOwner(OwnerKind.Page, "home") && JsonElement.DeepEquals(detachedHeader.Fields,
                proposed.Blocks.Single(b => b.Id == header.Id).Fields), "Detached values/ownership changed.");
            var updatedShared = detached.Website.Blocks.Single(b => b.Id == "site-header-root") with
                { Fields = JsonSerializer.SerializeToElement(new HeaderFields("Only remaining references change", detached.Website.Pages.Select(p => p.Id).ToImmutableArray()), CompositionJson.Options) };
            var afterShared = await store.CommitCompositionAsync(detached.Revision, detached.Website with { Blocks = detached.Website.Blocks.Replace(detached.Website.Blocks.Single(b => b.Id == updatedShared.Id), updatedShared) }, design);
            Require(JsonElement.DeepEquals((await store.ReadCompositionAsync()).Website.Blocks.Single(b => b.Id == reference.TargetId).Fields, detachedHeader.Fields), "Shared edit mutated detached copy.");
            var allDetached = afterShared;
            foreach (var page in afterShared.Website.Pages.Where(p => p.Id != "home"))
                allDetached = allDetached with { Website = CompositionCopies.DetachShared(allDetached, design, registry, DemoComposition.PlacementId("header", page.Id)) };
            var detachedAll = await store.CommitCompositionAsync(afterShared.Revision, allDetached.Website, design);
            var withoutHeader = CompositionCopies.DeleteUnreferencedShared(detachedAll, design, registry, "site-header");
            var deleted = await store.CommitCompositionAsync(detachedAll.Revision, withoutHeader, design);
            Require(!deleted.Website.SharedBlocks.Any(s => s.Id == "site-header"), "Unreferenced definition not deleted.");
            Console.WriteLine("PASS: Referenced shared deletion is rejected; detach copies independent content/IDs and permits later unreferenced deletion.");

            var oldImage = deleted.Website.Assets.Single();
            await Fails<ContentValidationException>(() => store.CommitCompositionAsync(deleted.Revision, deleted.Website, design,
                ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add(oldImage.File, [99])));
            Require((await store.ReadCompositionAsync()).Revision == deleted.Revision, "Rejected media replacement committed.");
            Require((string)(await Sql(path, "SELECT snapshot FROM revisions WHERE revision=$revision", ("$revision", legacy.Revision)))! == originalJson &&
                (string)(await Sql(path, "SELECT artifact FROM previews WHERE id=$id", ("$id", previewId)))! == originalPreviewJson, "Stored legacy/exported artifact representation changed.");
            Require((await store.ReadPreviewAsync(previewId))!.Digest == preview.Digest && (await store.ReadPreviewAsync(v2previewId))!.Digest == v2preview.Digest,
                "Retained artifacts changed across composition edits/deletion.");
            var restored = await store.RestoreLegacyAsync(deleted.Revision, legacy.Revision);
            Require(restored.Revision != legacy.Revision && restored.Revision != deleted.Revision && (await store.ReadAsync()).Website.Title == legacy.Website.Title,
                "Recovery lost v1 content or reused an old revision.");
            await Fails<RevisionConflictException>(async () => await store.UpdateDraftAsync(new(legacy.Revision, "home", "introduction", "heading", "Stale old form")));
            Require((await store.ReadCompositionRevisionAsync(deleted.Revision))!.Website.SharedBlocks.Length == 1, "Recovery deleted v2 history.");
            Console.WriteLine("PASS: Media paths/history/artifacts stay immutable; recovery creates a fresh v1 revision without deleting v2 history.");

            var newPath = Path.Combine(directory, "new.db");
            var fresh = new SqliteContentSource(newPath, registry); await fresh.InitializeSchemaAsync();
            var extraAssets = migrated.Website with { Assets = migrated.Website.Assets.Add(new("missing", "assets/missing.svg", "image/svg+xml")) };
            await Fails<ContentValidationException>(() => fresh.CreateCompositionAsync(extraAssets, design, assets.ToImmutable()));
            Require(await fresh.HeadAsync() is null && System.Convert.ToInt32(await Sql(newPath, "SELECT COUNT(*) FROM assets")) == 0, "Failed create left partial media/head.");
            var created = await fresh.CreateCompositionAsync(migrated.Website, design, assets.ToImmutable());
            await Fails<SiteAlreadyExistsException>(() => fresh.CreateCompositionAsync(migrated.Website, design, assets.ToImmutable()));
            Require((await fresh.ReadCompositionAsync()).Revision == created.Revision, "Fresh composition site missing.");
            Console.WriteLine("PASS: Fresh v2 creation validates and stores atomically without overwriting a site or leaving partial media.");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            async Task<(int Code, string Output, string Errors)> Command(string action, string? expected = null, string? original = null)
            {
                await using var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true,
                    contentAction: action, expectedRevision: expected, legacyRevision: original);
                await host.Process.WaitForExitAsync(timeout.Token);
                return (host.Process.ExitCode, await host.Output, await host.Errors);
            }
            var inspect = await Command("inspect"); Require(inspect.Code == 0 && inspect.Output.Contains(restored.Revision), "Offline inspect failed.");
            var noExpected = await Command("migrate"); Require(noExpected.Code != 0, "Migration accepted no revision.");
            var commandMigration = await Command("migrate", restored.Revision);
            Require(commandMigration.Code == 0, "Offline migration failed: " + commandMigration.Errors);
            var headV2 = (await store.HeadAsync())!;
            await using (var blocked = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await blocked.Process.WaitForExitAsync(timeout.Token);
                Require(blocked.Process.ExitCode != 0 && (await blocked.Errors).Contains("composition board/API arrive"), "v1 host served a migrated site unsafely.");
            }
            var staleRestore = await Command("restore", restored.Revision, restored.Revision); Require(staleRestore.Code != 0, "Stale recovery succeeded.");
            var recovery = await Command("restore", headV2.Revision, restored.Revision); Require(recovery.Code == 0 && (await store.HeadAsync())!.Version == 1, "Offline recovery failed.");
            Require(!File.Exists(Path.Combine(directory, "accounts.db")), "Offline content tooling initialized unrelated account storage.");
            Console.WriteLine("PASS: Actual offline inspect/migrate/restore enforce expected revisions, exit without HTTP hosting and guard the v1 board.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
