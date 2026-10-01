using System.Collections.Immutable;
using System.Text;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Rendering.Razor;
using Webspine.Designs.Fieldwork.Components;

namespace Webspine.Designs.Fieldwork;

public static class FieldworkPackage
{
    public static RazorDesignPackage Create()
    {
        ImmutableArray<byte> Asset(string file)
        {
            using var input = typeof(FieldworkPackage).Assembly.GetManifestResourceStream("fieldwork/" + file)
                ?? throw new ContentValidationException("Missing installed Fieldwork design asset.");
            using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray().ToImmutableArray();
        }
        var css = Asset("fieldwork.css"); var definitions = FieldworkContent.Definitions();
        var bodyTypes = ImmutableArray.Create("service-intro", "text", "image", "cta", "cards", "group", "faq");
        var design = new CompositionDesign("fieldwork", "1", new("service",
            [new("brand", ["service-brand"], 1, 1), new("content", bodyTypes, 1, 1000), new("contact", ["service-contact"], 1, 1)]),
            new(bodyTypes, ["stack", "row", "grid"], ["start", "center"], ["small", "medium"], 3), Encoding.UTF8.GetString(css.AsSpan()));
        return new(new("fieldwork", "1", 2, "webspine-razor", "1"), definitions, design, typeof(FieldworkDocument),
            [new("text", 1, typeof(ServiceText)), new("image", 1, typeof(ServiceImage)), new("cta", 1, typeof(ServiceInvitation)),
                new("cards", 1, typeof(ServiceList)), new("group", 1, typeof(ServiceGroup)), new("faq", 1, typeof(ServiceQuestions)),
                new("service-brand", 1, typeof(BusinessNavigation)), new("service-contact", 1, typeof(ContactPanel)), new("service-intro", 1, typeof(PageIntroduction))],
            ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add("assets/fieldwork.css", css).Add("assets/questions.js", Asset("questions.js")));
    }
}
