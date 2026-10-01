using System.Text.Json;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Management;

internal static class ContentMigration
{
    public static async Task<int> RunAsync(SqliteContentSource source, IConfiguration configuration, CompositionOperations operations)
    {
        try
        {
            var action = configuration["Content:Action"];
            if (action is not ("inspect" or "migrate" or "restore")) throw new ContentValidationException("Content:Action must be inspect, migrate or restore.");
            var head = await source.HeadAsync() ?? throw new SiteNotInitializedException();
            if (action == "inspect")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { head, history = await source.HistoryAsync() }, CompositionJson.Options));
                return 0;
            }
            var expected = configuration["Content:ExpectedRevision"];
            if (string.IsNullOrWhiteSpace(expected)) throw new ContentValidationException("Content:ExpectedRevision is required; inspect the site first.");
            if (action == "restore")
            {
                var original = configuration["Content:LegacyRevision"] ?? throw new ContentValidationException("Content:LegacyRevision is required for restore.");
                var restored = await source.RestoreLegacyAsync(expected, original);
                Console.WriteLine(JsonSerializer.Serialize(new { restored.Revision, contractVersion = 1, originalRevision = original }, CompositionJson.Options));
                return 0;
            }
            var design = operations.Design;
            var legacy = await source.ReadLegacyRevisionAsync(expected);
            var mappings = legacy is null ? [] : LegacyCompositionMapping.Identities(legacy);
            var bySection = mappings.ToDictionary(m => (m.PageId, m.SectionId));
            var result = await source.MigrateToCompositionAsync(expected, design, current => operations.ConvertLegacy(current,
                LegacyCompositionMapping.Blocks(current), (page, section) => { var m = bySection[(page, section)]; return (m.BlockId, m.PlacementId); }),
                mappings, rehearse: operations.RehearseAsync);
            Console.WriteLine(JsonSerializer.Serialize(result, CompositionJson.Options));
            return 0;
        }
        catch (Exception exception) when (exception is ContentValidationException or RevisionConflictException or SiteNotInitializedException or SourceOperationNotSupportedException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
