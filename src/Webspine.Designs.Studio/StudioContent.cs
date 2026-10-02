using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Content.Faq;
using Webspine.Content.Product;

namespace Webspine.Designs.Studio;

public sealed record HeaderFields([property: JsonRequired] string Subtitle, [property: JsonRequired] ImmutableArray<string> PageIds);
public sealed record FooterFields([property: JsonRequired] string Message);
public sealed record PageTitleFields;
public sealed record ProductCardFields([property: JsonRequired] string ProductId, string? Caption = null);

public static class StudioContent
{
    private static BlockTypeDescriptor Descriptor(string id) => new(id, 1, 2, "webspine-studio-content", "1", id + "-fields-v1", "none", false);
    public static BlockRegistry Definitions() => new(StandardContentTypes.Definitions.AddRange(new IBlockDefinition[]
    {
        new BlockDefinition<HeaderFields>(Descriptor("site-header"), (fields, context) =>
        {
            CompositionRules.Text(fields.Subtitle, 160);
            if (fields.PageIds.IsDefaultOrEmpty || fields.PageIds.Length > 100 || fields.PageIds.Distinct().Count() != fields.PageIds.Length)
                throw new ContentValidationException("Navigation needs unique page references.");
            foreach (var id in fields.PageIds) context.Page(id);
        }, new("Site header", "Website branding and navigation", [EditorField.Text("subtitle", "Brand subtitle", "Your website", 160),
            EditorField.List("pageIds", "Navigation pages", "Navigation page", EditorField.Choice("page", "Navigation page", "", EditorChoiceSource.Pages), 1, 100, true)])),
        new BlockDefinition<FooterFields>(Descriptor("site-footer"), (fields, _) => CompositionRules.Text(fields.Message, 300),
            new("Site footer", "Website name and footer message", [EditorField.Text("message", "Footer message", "Get in touch", 300, true)])),
        new BlockDefinition<PageTitleFields>(Descriptor("page-title"), (_, _) => { }, new("Page title", "Reuse this page's headline", [])),
        FaqContent.Definition,
        new BlockDefinition<ProductCardFields>(Descriptor("product-card"), (_, _) => { },
            new("Product card", "Show reusable product information. Changing the selected product affects only this card.",
                [EditorField.Record("productId", "Product", "product", 1, "name"), EditorField.Text("caption", "Card caption (optional)", "", 160) with { Required = false }]),
            new("product-card", 1, [new("productId", "product", 1)], ["caption"]))
    }), new([ProductContent.Definition]));
}
