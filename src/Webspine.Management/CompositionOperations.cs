using System.Collections.Immutable;
using System.Security.Claims;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Management;

internal sealed class CompositionOperations(ICompositionDraftPersistence source, SqliteContentSource previews,
    IDesignPackage package, ICompositionRenderer renderer,
    FrozenInputStore inputs, ILogger<CompositionOperations> logger)
{
    public ICompositionDraftPersistence Source => source;
    public CompositionDesign Design => package.Design;
    public BlockRegistry Registry => package.ContentTypes;
    public async Task<object> SchemaAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var snapshot = await ReadAsync(ct); var caps = source.CompositionCapabilities;
        bool Allowed(CompositionOperation operation, string? type = null, int version = 1)
        {
            if (operation != CompositionOperation.Read && !Permissions.Has(user, "content:write")) return false;
            if (operation == CompositionOperation.Share && !Permissions.Has(user, "content:shared:write")) return false;
            try { caps.Require(operation, type, version); return true; }
            catch (SourceOperationNotSupportedException) { return false; }
        }
        object Type(BlockTypeDescriptor descriptor)
        {
            var definition = Registry.Resolve(descriptor.Id, descriptor.Version); var editor = definition.Editor;
            System.Text.Json.JsonElement? defaults = null; string? unavailable = null;
            var choices = new Dictionary<string, ImmutableArray<EditorChoice>>(StringComparer.Ordinal);
            void Fields(ImmutableArray<EditorField> fields, string path)
            {
                foreach (var field in fields)
                {
                    var key = path.Length == 0 ? field.Name : path + "." + field.Name;
                    var values = ContentEditorContract.Choices(field, snapshot.Website, Design);
                    if (field.ChoiceSource != EditorChoiceSource.None || !values.IsEmpty) choices.Add(key, values);
                    if (field.Kind == EditorFieldKind.Repeat)
                    {
                        if (field.Item is null) Fields(field.ItemFields, key + "[]");
                        else choices.Add(key + "[]", ContentEditorContract.Choices(field.Item, snapshot.Website, Design));
                    }
                }
            }
            if (ContentEditorContract.Generic(editor))
            {
                Fields(editor!.Fields, "");
                try { defaults = ContentEditorContract.Defaults(editor, snapshot.Website, Design); }
                catch (ContentValidationException e) { unavailable = e.Message; }
            }
            else unavailable = "The basic board requires editing metadata or a specialized editor. Typed clients must supply the registered schema.";
            return new { descriptor.Id, descriptor.Version, descriptor.SchemaId, descriptor.Container, definition.Pattern, editor, defaults,
                resolvedChoices = choices, boardEditable = ContentEditorContract.Generic(editor) && Allowed(CompositionOperation.Update, descriptor.Id, descriptor.Version),
                boardCreatable = editor is { AllowCreate: true } && defaults is not null && Allowed(CompositionOperation.Create, descriptor.Id, descriptor.Version),
                unavailable, canCreate = editor is not { AllowCreate: false } && Allowed(CompositionOperation.Create, descriptor.Id, descriptor.Version),
                canUpdate = Allowed(CompositionOperation.Update, descriptor.Id, descriptor.Version) };
        }
        return new { contractVersion = 2, metadataVersion = 1, snapshot.Revision, package = package.Descriptor,
            Design.Layout, Design.Groups, operations = caps.Operations.Where(op => Allowed(op)).ToArray(),
            canWriteShared = Permissions.Has(user, "content:write") && Permissions.Has(user, "content:shared:write"),
            types = Registry.Descriptors.Select(Type).ToArray(),
            recordSchemas = Registry.Records.Descriptors.Select(d => new { descriptor = d, editor = Registry.Records.Resolve(d.Id, d.Version).Editor,
                canCreate = Registry.Records.Resolve(d.Id, d.Version).Editor.AllowCreate && Allowed(CompositionOperation.RecordCreate) && caps.RecordSchemas.Contains(d),
                canUpdate = Allowed(CompositionOperation.RecordUpdate) && caps.RecordSchemas.Contains(d),
                canDelete = Allowed(CompositionOperation.RecordDelete) && caps.RecordSchemas.Contains(d) }).ToArray() };
    }
    public async Task<CompositionSnapshot> ReadAsync(CancellationToken ct)
    {
        source.CompositionCapabilities.Require(CompositionOperation.Read);
        var snapshot = await source.ReadCompositionAsync(ct);
        if (snapshot.Source != source.Identity) throw new ContentValidationException("The selected CMS returned a different source identity.");
        CompositionContract.Validate(snapshot, Design, Registry);
        foreach (var record in snapshot.Website.Records) source.CompositionCapabilities.RequireRecord(CompositionOperation.Read, record.SchemaId, record.SchemaVersion);
        return snapshot;
    }
    public static CompositionAuthority Authority(ClaimsPrincipal user) => new(Permissions.Has(user, "content:write"), Permissions.Has(user, "content:shared:write"), Permissions.Has(user, "settings:write"));
    public async Task<CompositionSnapshot> EditAsync(string revision, CompositionEdit edit, ClaimsPrincipal user, ImmutableArray<string> acknowledged, CancellationToken ct)
    {
        var result = await new CompositionEditor(source, Design, Registry).ApplyAsync(revision, edit, Authority(user), acknowledged, ct);
        logger.LogInformation("Composition operation {Operation} by account {Account} credential {Credential}: {Revision}", edit.GetType().Name,
            user.FindFirstValue(ClaimTypes.NameIdentifier), user.FindFirst("webspine:credential")?.Value, result.Revision);
        return result;
    }
    public async Task<CompositionSnapshot> MediaAsync(string revision, byte[] bytes, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Permissions.Has(user, "content:write")) throw new CompositionPermissionException("Content editing permission is required.");
        source.CompositionCapabilities.Require(CompositionOperation.Media);
        var snapshot = await ReadAsync(ct);
        if (snapshot.Revision != revision) throw new CompositionRevisionException();
        var media = CompositionMedia.Png(bytes);
        var existing = snapshot.Website.Assets.FirstOrDefault(a => a.Id == media.Asset.Id);
        if (existing is not null)
        {
            if (existing != media.Asset) throw new ContentValidationException("Media identifier collision.");
            return snapshot;
        }
        return await source.CommitCompositionAsync(revision, snapshot.Website with { Assets = snapshot.Website.Assets.Add(media.Asset) },
            Design, ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add(media.Asset.File, media.Bytes), ct);
    }
    public async Task<(string Id, BuiltArtifact Artifact)> PreviewAsync(string revision, string prefix, CancellationToken ct)
    {
        source.CompositionCapabilities.RequireCapture();
        var frozen = await package.CaptureAsync(ct);
        var captured = await source.CaptureCompositionAsync(frozen.Design, ct);
        foreach (var block in captured.Content.Website.Blocks) source.CompositionCapabilities.Require(CompositionOperation.Read, block.TypeId, block.TypeVersion);
        foreach (var record in captured.Content.Website.Records) source.CompositionCapabilities.RequireRecord(CompositionOperation.Read, record.SchemaId, record.SchemaVersion);
        if (captured.Content.Revision != revision) throw new CompositionRevisionException();
        var id = Guid.NewGuid().ToString("N");
        var artifact = await renderer.BuildAsync(captured, frozen, prefix + id, ct);
        await inputs.SaveAsync(id, captured, frozen, ct);
        await previews.SavePreviewAsync(id, artifact, ct);
        return (id, artifact);
    }
}
