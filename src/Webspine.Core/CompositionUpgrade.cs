using System.Collections.Immutable;
using System.Text.Json;

namespace Webspine.Core.Composition;

// Trusted developer registrations, never expressions or field mappings supplied by an HTTP client.
public sealed record VersionedFieldMigration(string Id, int FromVersion, int ToVersion, Func<JsonElement, JsonElement> Convert);
public sealed record CompositionUpgradePlan(string Id, string FromDesignRevision, string ToDesignRevision,
    ImmutableArray<VersionedFieldMigration> RecordSchemas, ImmutableArray<VersionedFieldMigration> Patterns);

public static class CompositionUpgrade
{
    // The host supplies both installed packages and an explicitly selected plan. No implicit latest-version conversion.
    // Writes go through the same active CMS; its target registry must be configured before committing.
    public static async Task<CompositionSnapshot> ApplyAsync(ICompositionDraftPersistence source, string expectedRevision,
        CompositionDesign from, BlockRegistry oldTypes, CompositionDesign to, BlockRegistry newTypes,
        CompositionUpgradePlan plan, CompositionAuthority authority, CancellationToken ct = default)
    {
        if (!authority.ContentWrite || !authority.SharedWrite || !authority.SettingsWrite)
            throw new CompositionPermissionException("An explicit design migration requires content, shared-content and settings permissions.");
        CompositionRules.Identifier(plan.Id);
        if (plan.FromDesignRevision != from.Revision || plan.ToDesignRevision != to.Revision || from.Id != to.Id)
            throw new ContentValidationException("Migration plan does not match the selected design revisions.");
        source.CompositionCapabilities.RequireCapture();
        var snapshot = await source.ReadCompositionAsync(ct);
        if (snapshot.Source != source.Identity) throw new ContentValidationException("The selected CMS returned a different source identity.");
        if (snapshot.Revision != expectedRevision) throw new CompositionRevisionException();
        CompositionContract.Validate(snapshot, from, oldTypes);
        JsonElement Convert(ImmutableArray<VersionedFieldMigration> mappings, string id, int oldVersion, int newVersion, JsonElement values)
        {
            if (mappings.IsDefault) throw new ContentValidationException("Explicit migration registrations are required.");
            var matches = mappings.Where(m => m.Id == id && m.FromVersion == oldVersion && m.ToVersion == newVersion).ToArray();
            if (matches.Length != 1) throw new ContentValidationException("Breaking schema/input changes require exactly one explicit migration.");
            var result = matches[0].Convert(values.Clone());
            if (result.ValueKind != JsonValueKind.Object) throw new ContentValidationException("Migration must produce complete typed fields.");
            return result.Clone();
        }
        var records = snapshot.Website.Records.Select(record =>
        {
            var target = newTypes.Records.Descriptors.SingleOrDefault(d => d.Id == record.SchemaId)
                ?? throw new ContentValidationException("Migration cannot silently remove a Record schema.");
            source.CompositionCapabilities.RequireRecord(CompositionOperation.RecordUpdate, target.Id, target.Version);
            return target.Version == record.SchemaVersion ? record : record with { SchemaVersion = target.Version,
                Revision = Guid.NewGuid().ToString("N"), Fields = Convert(plan.RecordSchemas, record.SchemaId, record.SchemaVersion, target.Version, record.Fields) };
        }).ToImmutableArray();
        var blocks = snapshot.Website.Blocks.Select(block =>
        {
            var target = newTypes.Descriptors.SingleOrDefault(d => d.Id == block.TypeId)
                ?? throw new ContentValidationException("Migration cannot silently remove a Block or Pattern type.");
            source.CompositionCapabilities.Require(CompositionOperation.Update, target.Id, target.Version);
            if (target.Version == block.TypeVersion) return block;
            if (oldTypes.Resolve(block.TypeId, block.TypeVersion).Pattern is null || newTypes.Resolve(target.Id, target.Version).Pattern is null)
                throw new ContentValidationException("This migration boundary covers Pattern inputs and Record schemas only.");
            return block with { TypeVersion = target.Version, Fields = Convert(plan.Patterns, block.TypeId, block.TypeVersion, target.Version, block.Fields) };
        }).ToImmutableArray();
        var proposed = snapshot.Website with { Records = records, Blocks = blocks };
        CompositionContract.Validate(snapshot with { Website = proposed }, to, newTypes);
        return await source.CommitCompositionAsync(expectedRevision, proposed, to, cancellationToken: ct);
    }
}
