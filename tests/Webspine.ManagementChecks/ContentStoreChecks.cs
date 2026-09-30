using System.Collections.Immutable;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Demo;

static class ContentStoreChecks
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "content.db");
            var store = new SqliteContentSource(path);
            await store.InitializeSchemaAsync();
            Require(await store.TryReadAsync() is null, "Empty storage was implicitly seeded.");
            var fixture = new DemoContentSource(await File.ReadAllTextAsync(Path.Combine(DemoSite.FixtureDirectory, "site.json")));
            var source = await fixture.ReadAsync();
            var assets = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>();
            foreach (var asset in source.Website.Assets) assets.Add(asset.File, (await File.ReadAllBytesAsync(Path.Combine(DemoSite.FixtureDirectory, asset.File))).ToImmutableArray());
            var initial = await store.CreateAsync(source.Website, assets.ToImmutable());
            await Fails<SiteAlreadyExistsException>(() => store.CreateAsync(source.Website, assets.ToImmutable()));
            var reopened = new SqliteContentSource(path);
            await reopened.InitializeSchemaAsync();
            var captured = await reopened.CaptureAsync();
            Require(captured.Snapshot.Revision == initial.Revision && captured.Snapshot.Website.Pages.Length == 5 && captured.Assets.Values.Single().SequenceEqual(assets.Values.Single()), "Restart lost content or assets.");
            Console.WriteLine("PASS: SQLite setup is explicit, cannot overwrite a site and survives reopening with copied media.");

            var updated = await store.UpdateDraftAsync(new(initial.Revision, "home", "introduction", "heading", "Changed heading"));
            Require(updated.Revision != initial.Revision && ((TextSection)updated.Website.Pages[0].Sections[0]).Heading == "Changed heading", "Conditional adapter write failed.");
            await Fails<RevisionConflictException>(async () => await reopened.UpdateDraftAsync(new(initial.Revision, "home", "introduction", "heading", "Stale heading")));
            await Fails<ContentValidationException>(async () => await reopened.UpdateDraftAsync(new(updated.Revision, "home", "introduction", "css", "body{}")));
            await Fails<ContentValidationException>(async () => await reopened.UpdateDraftAsync(new(updated.Revision, "home", "introduction", "heading", "")));
            Require((await store.HistoryAsync()).Length == 2 && (await store.ReadAsync()).Revision == updated.Revision, "Rejected edits left partial history/head changes.");
            Console.WriteLine("PASS: Common adapter enforces atomic revisions and rejects stale/forbidden/invalid fields without partial writes.");

            async Task<bool> Compete(string heading)
            {
                try { await reopened.UpdateDraftAsync(new(updated.Revision, "home", "introduction", "heading", heading)); return true; }
                catch (RevisionConflictException) { return false; }
            }
            var contenders = await Task.WhenAll(Task.Run(() => Compete("Writer one")), Task.Run(() => Compete("Writer two")));
            Require(contenders.Count(success => success) == 1 && (await store.HistoryAsync()).Length == 3, "Concurrent writers both replaced the same revision.");
            var head = await store.ReadAsync();
            await Fails<ContentValidationException>(() => store.AddPageAsync(head.Revision, "Duplicate", "/", "Duplicate home"));
            var added = await store.AddPageAsync(head.Revision, "News", "/news/", "Our latest news");
            Require(added.Website.Pages.Length == 6, "New page was not stored.");
            Console.WriteLine("PASS: Concurrent writes choose one winner and duplicate routes are rejected.");

            var previewId = Guid.NewGuid().ToString("N");
            captured = await store.CaptureAsync();
            var preview = await DemoSite.BuildSnapshotAsync(captured.Snapshot, captured.Assets, "/manage/preview/" + previewId);
            await store.SavePreviewAsync(previewId, preview);
            await store.UpdateDraftAsync(new(added.Revision, "home", "introduction", "heading", "After preview"));
            var retained = await reopened.ReadPreviewAsync(previewId);
            Require(retained is not null && retained.Digest == preview.Digest && retained.Files.Zip(preview.Files).All(pair => pair.First.Bytes.SequenceEqual(pair.Second.Bytes)), "Preview changed after editing/reopening.");
            await Fails<RevisionConflictException>(() => store.SavePreviewAsync(Guid.NewGuid().ToString("N"), preview));
            Console.WriteLine("PASS: Retained previews preserve exact bytes across edits/reopening and stale builds cannot be recorded.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Fails<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
