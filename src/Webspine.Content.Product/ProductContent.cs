using System.Text.Json.Serialization;
using Webspine.Core.Composition;

namespace Webspine.Content.Product;

public sealed record ProductFields([property: JsonRequired] string Name, string? Description = null, string? Image = null);

// Presentation-independent CMS content. No pricing, stock or commerce workflows.
public static class ProductContent
{
    public static IRecordDefinition Definition => Create();
    public static IRecordDefinition Create(bool allowCreate = true) => new RecordDefinition<ProductFields>(new("product", 1, "webspine-product-content", "1"),
        new("Product", "Reusable product information for your website", [EditorField.Text("name", "Product name", "New product", 160),
            EditorField.Text("description", "Description", "", 3000, true) with { Required = false }, EditorField.Image("image", "Product image", false)], "name", AllowCreate: allowCreate),
        (fields, context) =>
        {
            CompositionRules.Text(fields.Name, 160);
            if (fields.Image is not null) context.Asset(fields.Image);
        });
}
