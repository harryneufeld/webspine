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
            var starter = Webspine.Examples.StudioExample.Start("Store proof", true);
            var assets = starter.Assets;
            var migrated = await store.CreateCompositionAsync(starter.Composition, design, assets);
            var capture = await store.CaptureCompositionAsync(design);
            var built = new CompositionBuildPipeline(registry).Build(capture);
            Require(capture.AssetFiles.Single().Value.SequenceEqual(assets.Single().Value), "Capture changed media.");
            Console.WriteLine("PASS: Native v2 creation captures all pages, shared shell and exact media.");
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
            Require(winners.Count(v => v) == 1 && (await store.HistoryAsync()).Length == 3, "Concurrent composition writes both committed.");
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
            Require((await store.ReadPreviewAsync(v2previewId))!.Digest == v2preview.Digest, "Retained artifact changed across composition edits/deletion.");
            Require((await store.ReadCompositionRevisionAsync(migrated.Revision))!.Website.Title == migrated.Website.Title, "Edits changed historical content.");
            Console.WriteLine("PASS: Media paths, history and captured artifacts stay immutable across edits and deletion.");
            var newPath = Path.Combine(directory, "new.db");
            var fresh = new SqliteContentSource(newPath, registry); await fresh.InitializeSchemaAsync();
            var extraAssets = migrated.Website with { Assets = migrated.Website.Assets.Add(new("missing", "assets/missing.svg", "image/svg+xml")) };
            await Fails<ContentValidationException>(() => fresh.CreateCompositionAsync(extraAssets, design, assets));
            Require(await fresh.HeadAsync() is null && System.Convert.ToInt32(await Sql(newPath, "SELECT COUNT(*) FROM assets")) == 0, "Failed create left partial media/head.");
            var created = await fresh.CreateCompositionAsync(migrated.Website, design, assets);
            await Fails<SiteAlreadyExistsException>(() => fresh.CreateCompositionAsync(migrated.Website, design, assets));
            Require((await fresh.ReadCompositionAsync()).Revision == created.Revision, "Fresh composition site missing.");
            Console.WriteLine("PASS: Fresh v2 creation validates and stores atomically without overwriting a site or leaving partial media.");

        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
