using System.Text.Json.Serialization;
using Webspine.Core.Composition;

namespace Webspine.Designs.Studio;

public sealed record EntryFields([property: JsonRequired] string Title, string? Description = null, string? Image = null);

// A neutral starter schema; installations register their own domain schemas through RecordRegistry.
public static class EntryContent
{
    public static IRecordDefinition Definition => new RecordDefinition<EntryFields>(new("content-entry", 1, "webspine-studio-content", "1"),
        new("Content entry", "A title, description and optional image for any subject", [EditorField.Text("title", "Title", "New entry", 160),
            EditorField.Text("description", "Description", "", 3000, true) with { Required = false }, EditorField.Image("image", "Image", false)], "title"),
        (fields, context) =>
        {
            CompositionRules.Text(fields.Title, 160);
            if (fields.Image is not null) context.Asset(fields.Image);
        });
}
