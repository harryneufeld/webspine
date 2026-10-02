using System.Collections.Immutable;
using System.Text;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Rendering.Razor;
using Webspine.Designs.Studio.Components;

namespace Webspine.Designs.Studio;

public static class StudioPackage
{
    public static RazorDesignPackage Create()
    {
        ImmutableArray<byte> Asset(string file)
        {
            using var stream = typeof(StudioPackage).Assembly.GetManifestResourceStream("studio/" + file)
                ?? throw new ContentValidationException("Missing installed Studio design asset.");
            using var bytes = new MemoryStream(); stream.CopyTo(bytes); return ImmutableArray.Create(bytes.ToArray());
        }
        var css = Asset("site.css"); var script = Asset("site.js");
        var definitions = StudioContent.Definitions();
        var all = definitions.Descriptors.Select(d => d.Id).ToImmutableArray();
        var design = new CompositionDesign("studio", "1", new("standard",
            [new("header", ["site-header", "group"], 1, 10), new("main", ["text", "image", "cta", "cards", "group", "page-title", "faq", "product-card"], 1, 1000),
                new("footer", ["site-footer", "group"], 1, 10)]),
            new(all, ["stack", "row", "grid"], ["start", "center"], ["small", "medium"], 4), Encoding.UTF8.GetString(css.AsSpan()));
        return new(new("studio", "1", 2, "webspine-razor", "1"), definitions, design, typeof(StudioDocument),
            [new("text", 1, typeof(TextBlock)), new("image", 1, typeof(ImageBlock)), new("cta", 1, typeof(CallToAction)),
             new("cards", 1, typeof(Cards)), new("group", 1, typeof(Group)), new("site-header", 1, typeof(SiteHeader)),
             new("site-footer", 1, typeof(SiteFooter)), new("page-title", 1, typeof(PageTitle)), new("faq", 1, typeof(FrequentlyAsked)), new("product-card", 1, typeof(ProductCard))],
            ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add("assets/composition.css", css).Add("assets/site.js", script));
    }
}
