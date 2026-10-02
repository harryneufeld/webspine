using System.Collections.Immutable;
using System.Text.Json;

namespace Webspine.Core.Composition;

public sealed record ContentRecord(string Id, string SchemaId, int SchemaVersion, string Revision, JsonElement Fields);
public sealed record RecordSchemaDescriptor(string Id, int Version, string ModuleId, string ModuleVersion);
public interface IRecordDefinition
{
    RecordSchemaDescriptor Descriptor { get; }
    Type PayloadType { get; }
    ContentEditorMetadata Editor { get; }
    string ImplementationDigest { get; }
    JsonElement Values(ContentRecord record, CompositionWebsite site, CompositionDesign design);
}

// Reuse strict typed field validation; Records are data and never renderer registrations.
public sealed class RecordDefinition<T>(RecordSchemaDescriptor descriptor, ContentEditorMetadata editor,
    Action<T, BlockValidationContext> validate) : IRecordDefinition
{
    private readonly BlockDefinition<T> fields = new(new(descriptor.Id, descriptor.Version, 2, descriptor.ModuleId,
        descriptor.ModuleVersion, descriptor.Id + "-record-v" + descriptor.Version, "none", false), validate, editor);
    public RecordSchemaDescriptor Descriptor => descriptor;
    public Type PayloadType => typeof(T);
    public ContentEditorMetadata Editor => editor;
    public string ImplementationDigest => fields.ImplementationDigest;
    public JsonElement Values(ContentRecord record, CompositionWebsite site, CompositionDesign design)
    {
        if (record.Fields.ValueKind != JsonValueKind.Object || record.Fields.GetRawText().Length > 65536)
            throw new ContentValidationException("Record fields require a bounded object.");
        var normalized = ContentEditorContract.OptionalDefaults(editor, record.Fields);
        fields.Validate(new(record.Id, new(OwnerKind.Page, site.Pages[0].Id), descriptor.Id, descriptor.Version, normalized, []), new(site, design));
        return normalized;
    }
}

public sealed class RecordRegistry
{
    private readonly ImmutableDictionary<string, IRecordDefinition> definitions;
    public ImmutableArray<RecordSchemaDescriptor> Descriptors { get; }
    public RecordRegistry(IEnumerable<IRecordDefinition>? registrations = null)
    {
        var items = (registrations ?? []).OrderBy(d => d.Descriptor.Id, StringComparer.Ordinal).ToArray();
        if (items.Length > 100) throw new ContentValidationException("Record schema registrations exceed their bound.");
        var map = ImmutableDictionary.CreateBuilder<string, IRecordDefinition>(StringComparer.Ordinal);
        foreach (var definition in items)
        {
            var d = definition.Descriptor;
            CompositionRules.Identifier(d.Id); CompositionRules.Identifier(d.ModuleId); CompositionRules.Text(d.ModuleVersion, 80);
            ContentEditorContract.ValidateRegistration(definition.Editor, definition.PayloadType);
            if (ContentEditorContract.AllFields(definition.Editor.Fields).Any(f => f.ChoiceSource == EditorChoiceSource.Records))
                throw new ContentValidationException("Record-to-Record references are outside the supported content schema boundary.");
            if (d.Version < 1 || !ContentEditorContract.Generic(definition.Editor) || !map.TryAdd(d.Id, definition))
                throw new ContentValidationException("Duplicate or unsupported Record schema.");
        }
        definitions = map.ToImmutable(); Descriptors = items.Select(d => d.Descriptor).ToImmutableArray();
    }
    public IRecordDefinition Resolve(string id, int version) => definitions.TryGetValue(id, out var definition) && definition.Descriptor.Version == version
        ? definition : throw new ContentValidationException("Unavailable Record schema/version. An explicit migration is required for incompatible versions.");
    public void Validate(CompositionWebsite site, CompositionDesign design)
    {
        if (site.Records.IsDefault || site.Records.Length > 1000 || site.Records.Any(r => r is null))
            throw new ContentValidationException("Invalid or oversized Record collection.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in site.Records)
        {
            CompositionRules.Identifier(record.Id); CompositionRules.Text(record.Revision, 256);
            if (!ids.Add(record.Id)) throw new ContentValidationException("Duplicate Record identity.");
            Resolve(record.SchemaId, record.SchemaVersion).Values(record, site, design);
        }
    }
}

public sealed record RecordBinding(string Input, string SchemaId, int SchemaVersion);
public sealed record PatternDescriptor(string Id, int Version, ImmutableArray<RecordBinding> RecordInputs,
    ImmutableArray<string> OverrideSlots);

public static class PatternContract
{
    public static void ValidateRegistration(IBlockDefinition definition, RecordRegistry records)
    {
        var recordFields = definition.Editor is { } editor ? ContentEditorContract.AllFields(editor.Fields).Where(f => f.ChoiceSource == EditorChoiceSource.Records).ToArray() : [];
        if (definition.Pattern is not { } pattern)
        {
            if (recordFields.Length > 0) throw new ContentValidationException("Record references require declared Pattern inputs for impact and permission enforcement.");
            return;
        }
        if (pattern.Id != definition.Descriptor.Id || pattern.Version != definition.Descriptor.Version || definition.Descriptor.Container ||
            pattern.RecordInputs.IsDefaultOrEmpty || pattern.OverrideSlots.IsDefault || !ContentEditorContract.Generic(definition.Editor) ||
            recordFields.Length != pattern.RecordInputs.Length || recordFields.Any(f => !pattern.RecordInputs.Any(b => b.Input == f.Name)))
            throw new ContentValidationException("A Pattern requires a versioned, bounded typed input contract.");
        var inputs = pattern.RecordInputs.Select(b => b.Input).Concat(pattern.OverrideSlots).ToArray();
        if (inputs.Distinct(StringComparer.Ordinal).Count() != inputs.Length ||
            !inputs.Order(StringComparer.Ordinal).SequenceEqual(definition.Editor!.Fields.Select(f => f.Name).Order(StringComparer.Ordinal)))
            throw new ContentValidationException("Only declared Pattern inputs and override slots may be edited.");
        foreach (var binding in pattern.RecordInputs)
        {
            var schema = records.Resolve(binding.SchemaId, binding.SchemaVersion);
            var field = definition.Editor.Fields.Single(f => f.Name == binding.Input);
            if (!field.Required || field.Kind != EditorFieldKind.Choice || field.ChoiceSource != EditorChoiceSource.Records ||
                field.RecordSchemaId != binding.SchemaId || field.RecordSchemaVersion != binding.SchemaVersion)
                throw new ContentValidationException("Pattern Record bindings must match their typed editor references.");
            if (field.RecordLabelField is { } label && !schema.Editor.Fields.Any(f => f.Name == label && f.Kind == EditorFieldKind.Text))
                throw new ContentValidationException("Record choice labels require a declared text field.");
        }
    }
    public static void Validate(Block block, IBlockDefinition definition, CompositionWebsite site)
    {
        if (definition.Pattern is not { } pattern) return;
        foreach (var binding in pattern.RecordInputs)
        {
            if (!block.Fields.TryGetProperty(binding.Input, out var value) || value.ValueKind != JsonValueKind.String ||
                !site.Records.Any(r => r.Id == value.GetString() && r.SchemaId == binding.SchemaId && r.SchemaVersion == binding.SchemaVersion))
                throw new ContentValidationException("Pattern references missing or incompatible required Record data.");
        }
    }
    public static ImmutableArray<string> AffectedPages(CompositionWebsite site, BlockRegistry registry, string recordId)
    {
        var bound = site.Blocks.Where(b => References(b, registry, recordId)).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var blocks = site.Blocks.ToDictionary(b => b.Id); var shared = site.SharedBlocks.ToDictionary(s => s.Id);
        bool Uses(Placement p)
        {
            var id = p.Kind == TargetKind.Shared ? shared[p.TargetId].RootBlockId : p.TargetId;
            return bound.Contains(id) || blocks[id].Children.Any(Uses);
        }
        return site.Pages.Where(p => p.Regions.SelectMany(r => r.Placements).Any(Uses)).Select(p => p.Id).Order(StringComparer.Ordinal).ToImmutableArray();
    }
    public static bool IsReferenced(CompositionWebsite site, BlockRegistry registry, string recordId) => site.Blocks.Any(b => References(b, registry, recordId));
    private static bool References(Block block, BlockRegistry registry, string recordId) => registry.Resolve(block.TypeId, block.TypeVersion).Pattern?.RecordInputs.Any(i =>
        block.Fields.TryGetProperty(i.Input, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == recordId) == true;
}
