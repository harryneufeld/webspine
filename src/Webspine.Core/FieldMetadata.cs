using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Webspine.Core.Composition;

public enum EditorFieldKind { Text, Image, Destination, Choice, Integer, Repeat }
public enum EditorChoiceSource { None, Images, Pages, GroupModes, GroupAlignments, GroupSpacing, GroupColumns, Records }
public sealed record EditorChoice(string Value, string Label);
public sealed record EditorField(string Name, string Label, EditorFieldKind Kind, bool Required, JsonElement Default,
    int MaximumLength = 0, bool Multiline = false, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<EditorChoice> Choices = default,
    EditorChoiceSource ChoiceSource = EditorChoiceSource.None, int Minimum = 0, int Maximum = 0,
    string? ItemLabel = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<EditorField> ItemFields = default, EditorField? Item = null,
    int DefaultItemCount = 0, bool DefaultAllPages = false, string? RecordSchemaId = null, int RecordSchemaVersion = 0, string? RecordLabelField = null)
{
    private static JsonElement Value(object? value) => JsonSerializer.SerializeToElement(value, CompositionJson.Options);
    public static EditorField Text(string name, string label, string initial, int maximum, bool multiline = false) =>
        new(name, label, EditorFieldKind.Text, true, Value(initial), maximum, multiline);
    public static EditorField Image(string name, string label, bool required = true) =>
        new(name, label, EditorFieldKind.Image, required, Value(null), 64, ChoiceSource: EditorChoiceSource.Images);
    public static EditorField Destination(string name, string label, string initial = "/") =>
        new(name, label, EditorFieldKind.Destination, true, Value(initial), 500);
    public static EditorField Record(string name, string label, string schema, int version, string? labelField = null) =>
        new(name, label, EditorFieldKind.Choice, true, Value(null), 64, ChoiceSource: EditorChoiceSource.Records,
            RecordSchemaId: schema, RecordSchemaVersion: version, RecordLabelField: labelField);
    public static EditorField Choice(string name, string label, string initial, EditorChoiceSource source) =>
        new(name, label, EditorFieldKind.Choice, true, Value(initial), 160, ChoiceSource: source);
    public static EditorField Number(string name, string label, int initial, int minimum, int maximum,
        EditorChoiceSource source = EditorChoiceSource.None) =>
        new(name, label, EditorFieldKind.Integer, true, Value(initial), ChoiceSource: source, Minimum: minimum, Maximum: maximum);
    public static EditorField Items(string name, string label, string itemLabel, ImmutableArray<EditorField> fields,
        int minimum, int maximum, int initialCount = 1) =>
        new(name, label, EditorFieldKind.Repeat, true, Value(null), Minimum: minimum, Maximum: maximum,
            ItemLabel: itemLabel, ItemFields: fields, DefaultItemCount: initialCount);
    public static EditorField List(string name, string label, string itemLabel, EditorField item,
        int minimum, int maximum, bool allPages = false) =>
        new(name, label, EditorFieldKind.Repeat, true, Value(null), Minimum: minimum, Maximum: maximum,
            ItemLabel: itemLabel, Item: item, DefaultItemCount: minimum, DefaultAllPages: allPages);
}

// A data-only contract; specialized editors are explicitly unsupported by the basic board.
public sealed record ContentEditorMetadata(string Label, string Description, ImmutableArray<EditorField> Fields,
    string? SummaryField = null, string? SpecializedEditor = null, int Version = 1, bool AllowCreate = true);

public static class ContentEditorContract
{
    public static IEnumerable<EditorField> AllFields(ImmutableArray<EditorField> fields)
    {
        foreach (var field in fields)
        {
            yield return field;
            if (!field.ItemFields.IsDefaultOrEmpty) foreach (var nested in AllFields(field.ItemFields)) yield return nested;
            if (field.Item is not null) foreach (var nested in AllFields([field.Item])) yield return nested;
        }
    }
    public static bool Generic(ContentEditorMetadata? editor) => editor is { SpecializedEditor: null };
    public static void ValidateRegistration(ContentEditorMetadata editor, Type payload)
    {
        CompositionRules.Text(editor.Label, 120); CompositionRules.Text(editor.Description, 500);
        if (editor.Version != 1 || editor.Fields.IsDefault) Fail("Unsupported editor metadata version or fields.");
        if (editor.SpecializedEditor is not null)
        {
            CompositionRules.Identifier(editor.SpecializedEditor);
            if (!editor.Fields.IsEmpty) Fail("Specialized editors cannot also declare basic fields.");
            return;
        }
        var total = 0;
        void Fields(ImmutableArray<EditorField> fields, Type type, int depth)
        {
            if (depth > 3 || fields.IsDefault || fields.Length > 32) Fail("Editor metadata exceeds its bounds.");
            var properties = type.GetProperties().Where(p => p.GetMethod?.IsPublic == true &&
                p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always)
                .ToDictionary(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name), StringComparer.Ordinal);
            if (fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length ||
                properties.Count != fields.Length || fields.Any(f => !properties.ContainsKey(f.Name))) Fail("Basic metadata must describe every serialized property exactly once.");
            foreach (var field in fields) Field(field, properties[field.Name].PropertyType, depth);
        }
        void Field(EditorField field, Type type, int depth)
        {
            if (++total > 64) Fail("Editor metadata exceeds its field count bound.");
            if (string.IsNullOrEmpty(field.Name) || field.Name.Length > 64 || !char.IsAsciiLetterLower(field.Name[0]) ||
                field.Name.Any(c => !char.IsAsciiLetterOrDigit(c))) Fail("Invalid editor field name.");
            CompositionRules.Text(field.Label, 120);
            if (!Enum.IsDefined(field.Kind) || !Enum.IsDefined(field.ChoiceSource) || field.Default.ValueKind == JsonValueKind.Undefined) Fail("Invalid field kind/default/source.");
            if (field.ChoiceSource == EditorChoiceSource.Records)
            {
                CompositionRules.Identifier(field.RecordSchemaId);
                if (field.Kind != EditorFieldKind.Choice || field.RecordSchemaVersion < 1) Fail("Record choices require an explicit schema version.");
            }
            else if (field.RecordSchemaId is not null || field.RecordSchemaVersion != 0 || field.RecordLabelField is not null) Fail("Only Record choices may declare a Record schema.");
            if (field.Multiline && field.Kind != EditorFieldKind.Text) Fail("Only text fields support multiline editing.");
            if (field.Kind == EditorFieldKind.Choice && field.ChoiceSource is EditorChoiceSource.GroupColumns) Fail("Column choices require an integer field.");
            if (field.Kind == EditorFieldKind.Integer && field.ChoiceSource is not (EditorChoiceSource.None or EditorChoiceSource.GroupColumns)) Fail("Integer fields require numeric choices.");
            if (field.Kind == EditorFieldKind.Choice && field.ChoiceSource == EditorChoiceSource.None && field.Choices.IsDefaultOrEmpty) Fail("Approved choices require a declared list or source.");
            if (field.Kind == EditorFieldKind.Image && field.ChoiceSource != EditorChoiceSource.Images) Fail("Image fields require the captured image-reference source.");
            if (field.Kind is EditorFieldKind.Text or EditorFieldKind.Destination or EditorFieldKind.Repeat && (field.ChoiceSource != EditorChoiceSource.None || !field.Choices.IsDefaultOrEmpty)) Fail("This field kind cannot declare choices.");
            if (!field.Choices.IsDefault && (field.Choices.Length > 100 || field.Choices.Select(c => c.Value).Distinct().Count() != field.Choices.Length)) Fail("Invalid choices.");
            foreach (var choice in field.Choices.IsDefault ? [] : field.Choices) { CompositionRules.Text(choice.Value, 160); CompositionRules.Text(choice.Label, 120); }
            if (field.Kind == EditorFieldKind.Repeat)
            {
                CompositionRules.Text(field.ItemLabel, 120);
                if (!field.Required || field.Minimum < 0 || field.Maximum < Math.Max(1, field.Minimum) || field.Maximum > 100 ||
                    field.DefaultItemCount < field.Minimum || field.DefaultItemCount > field.Maximum ||
                    !type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ImmutableArray<>)) Fail("Invalid bounded list metadata.");
                var itemType = type.GetGenericArguments()[0];
                if (field.Item is not null)
                {
                    if (!field.ItemFields.IsDefaultOrEmpty || field.Item.Kind == EditorFieldKind.Repeat) Fail("Invalid list item metadata.");
                    Field(field.Item, itemType, depth + 1);
                }
                else Fields(field.ItemFields, itemType, depth + 1);
                if (field.DefaultAllPages && field.Item?.ChoiceSource != EditorChoiceSource.Pages) Fail("All-pages defaults require page-reference items.");
            }
            else
            {
                if (field.Item is not null || !field.ItemFields.IsDefaultOrEmpty || field.DefaultAllPages) Fail("Scalar fields cannot carry list metadata.");
                if (field.Kind == EditorFieldKind.Integer)
                {
                    if (type != typeof(int) || field.Minimum > field.Maximum || field.Default.ValueKind != JsonValueKind.Number || !field.Default.TryGetInt32(out var n) || n < field.Minimum || n > field.Maximum) Fail("Invalid integer metadata.");
                    foreach (var choice in field.Choices.IsDefault ? [] : field.Choices)
                        if (!int.TryParse(choice.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number) || number < field.Minimum || number > field.Maximum) Fail("Integer choices must fit the declared numeric bounds.");
                }
                else if (type != typeof(string) || field.MaximumLength < 1 || field.MaximumLength > 5000 ||
                    field.Default.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                    (field.Default.ValueKind == JsonValueKind.String && field.Default.GetString()!.Length > field.MaximumLength)) Fail("Invalid string metadata.");
                if (field.Kind is EditorFieldKind.Text or EditorFieldKind.Destination && field.Required &&
                    (field.Default.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(field.Default.GetString()))) Fail("Required text/link fields need usable defaults.");
            }
        }
        Fields(editor.Fields, payload, 0);
        if (editor.SummaryField is not null && !editor.Fields.Any(f => f.Name == editor.SummaryField && f.Kind == EditorFieldKind.Text)) Fail("Summary requires a text field.");
    }

    public static ImmutableArray<EditorChoice> Choices(EditorField field, CompositionWebsite site, CompositionDesign design) => field.ChoiceSource switch
    {
        EditorChoiceSource.Images => site.Assets.Select((a, i) => new EditorChoice(a.Id, "Image " + (i + 1))).ToImmutableArray(),
        EditorChoiceSource.Pages => site.Pages.Select(p => new EditorChoice(p.Id, p.Title)).ToImmutableArray(),
        EditorChoiceSource.Records => site.Records.Where(r => r.SchemaId == field.RecordSchemaId && r.SchemaVersion == field.RecordSchemaVersion)
            .Select(r => new EditorChoice(r.Id, field.RecordLabelField is { } label && r.Fields.TryGetProperty(label, out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : r.Id)).ToImmutableArray(),
        EditorChoiceSource.GroupModes => design.Groups.Modes.Select(v => new EditorChoice(v, Label(v))).ToImmutableArray(),
        EditorChoiceSource.GroupAlignments => design.Groups.Alignments.Select(v => new EditorChoice(v, Label(v))).ToImmutableArray(),
        EditorChoiceSource.GroupSpacing => design.Groups.Spacing.Select(v => new EditorChoice(v, Label(v))).ToImmutableArray(),
        EditorChoiceSource.GroupColumns => Enumerable.Range(1, design.Groups.MaximumColumns).Select(n => new EditorChoice(n.ToString(System.Globalization.CultureInfo.InvariantCulture), n.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToImmutableArray(),
        _ => field.Choices.IsDefault ? [] : field.Choices
    };
    private static string Label(string text) => char.ToUpperInvariant(text[0]) + text[1..].Replace('-', ' ');
    public static JsonElement Defaults(ContentEditorMetadata? editor, CompositionWebsite site, CompositionDesign design)
    {
        if (!Generic(editor)) throw new SourceOperationNotSupportedException("This type requires a specialized editor or a client with its typed schema. The basic board cannot safely edit it.");
        var values = JsonSerializer.SerializeToElement(ObjectDefaults(editor!.Fields, site, design), CompositionJson.Options);
        ValidateValues(editor, values, site, design); return values;
    }
    public static JsonElement OptionalDefaults(ContentEditorMetadata editor, JsonElement fields)
    {
        if (fields.ValueKind != JsonValueKind.Object) throw new ContentValidationException("Typed fields require an object.");
        CompositionJson.RejectDuplicateProperties(fields);
        var values = (JsonObject)JsonNode.Parse(fields.GetRawText())!;
        foreach (var field in editor.Fields.Where(f => !f.Required))
            if (!values.ContainsKey(field.Name) || values[field.Name] is null)
                values[field.Name] = JsonNode.Parse(field.Default.GetRawText());
        return JsonSerializer.SerializeToElement(values, CompositionJson.Options);
    }
    public static JsonObject ObjectDefaults(ImmutableArray<EditorField> fields, CompositionWebsite site, CompositionDesign design)
    {
        var result = new JsonObject(); foreach (var field in fields) result[field.Name] = Default(field, site, design); return result;
    }
    public static JsonNode? ItemDefault(EditorField field, CompositionWebsite site, CompositionDesign design) =>
        field.Item is null ? ObjectDefaults(field.ItemFields, site, design) : Default(field.Item, site, design);
    private static JsonNode? Default(EditorField field, CompositionWebsite site, CompositionDesign design)
    {
        if (field.Kind == EditorFieldKind.Repeat)
        {
            if (field.DefaultAllPages) return new JsonArray(site.Pages.Select(p => (JsonNode?)JsonValue.Create(p.Id)).ToArray());
            return new JsonArray(Enumerable.Range(0, field.DefaultItemCount).Select(_ => ItemDefault(field, site, design)).ToArray());
        }
        var choices = Choices(field, site, design);
        if (field.ChoiceSource != EditorChoiceSource.None || !choices.IsEmpty)
        {
            if (!field.Required && field.Default.ValueKind == JsonValueKind.Null) return null;
            var initial = field.Default.ValueKind == JsonValueKind.Null ? "" : field.Default.ToString();
            var selected = choices.FirstOrDefault(c => c.Value == initial) ?? choices.FirstOrDefault() ?? throw new ContentValidationException("No reference or choice is available for " + field.Label + ".");
            return field.Kind == EditorFieldKind.Integer ? JsonValue.Create(int.Parse(selected.Value, System.Globalization.CultureInfo.InvariantCulture)) : JsonValue.Create(selected.Value);
        }
        return JsonNode.Parse(field.Default.GetRawText());
    }
    public static void ValidateValues(ContentEditorMetadata editor, JsonElement values, CompositionWebsite site, CompositionDesign design)
    {
        if (!Generic(editor)) return;
        void Fields(ImmutableArray<EditorField> fields, JsonElement obj)
        {
            if (obj.ValueKind != JsonValueKind.Object || obj.EnumerateObject().Any(p => !fields.Any(f => f.Name == p.Name)) || fields.Any(f => f.Required && !obj.TryGetProperty(f.Name, out _))) Fail("Fields do not match the complete registered editing contract.");
            foreach (var field in fields) if (obj.TryGetProperty(field.Name, out var value)) Field(field, value);
        }
        void Field(EditorField field, JsonElement value)
        {
            if (!field.Required && value.ValueKind == JsonValueKind.Null) return;
            if (field.Kind == EditorFieldKind.Repeat)
            {
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < field.Minimum || value.GetArrayLength() > field.Maximum) Fail(field.Label + " has an invalid item count.");
                foreach (var item in value.EnumerateArray()) { if (field.Item is null) Fields(field.ItemFields, item); else Field(field.Item, item); }
                return;
            }
            if (field.Kind == EditorFieldKind.Integer)
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n) || n < field.Minimum || n > field.Maximum) Fail(field.Label + " requires a whole number within its limits.");
            }
            else if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > field.MaximumLength || (field.Required && string.IsNullOrWhiteSpace(value.GetString()))) Fail(field.Label + " is required and must fit its limit.");
            if (field.Kind == EditorFieldKind.Destination) CompositionRules.Destination(value.GetString()!, site.Pages.Select(p => p.Path).ToHashSet(StringComparer.Ordinal));
            var choices = Choices(field, site, design);
            if ((field.ChoiceSource != EditorChoiceSource.None || !choices.IsEmpty) && !choices.Any(c => c.Value == value.ToString())) Fail("Choose an available value for " + field.Label + ".");
        }
        Fields(editor.Fields, values);
    }
    private static void Fail(string message) => throw new ContentValidationException(message);
}
