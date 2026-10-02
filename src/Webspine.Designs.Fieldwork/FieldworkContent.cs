using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Content.Faq;

namespace Webspine.Designs.Fieldwork;

public sealed record BrandFields([property: JsonRequired] string Area, [property: JsonRequired] string Tagline,
    [property: JsonRequired] ImmutableArray<string> PageIds);
public sealed record ContactFields([property: JsonRequired] string Heading, [property: JsonRequired] string Text,
    [property: JsonRequired] string Label, [property: JsonRequired] string Destination);
public sealed record IntroFields([property: JsonRequired] string Kicker);

public static class FieldworkContent
{
    private static BlockTypeDescriptor Descriptor(string id) => new(id, 1, 2, "webspine-fieldwork-content", "1", id + "-fields-v1", "none", false);
    public static BlockRegistry Definitions() => new(StandardContentTypes.Definitions.AddRange(new IBlockDefinition[]
    {
        new BlockDefinition<BrandFields>(Descriptor("service-brand"), (f, c) =>
        {
            CompositionRules.Text(f.Area, 160); CompositionRules.Text(f.Tagline, 200);
            if (f.PageIds.IsDefaultOrEmpty || f.PageIds.Length > 20 || f.PageIds.Distinct().Count() != f.PageIds.Length)
                throw new ContentValidationException("Service navigation requires 1–20 unique pages.");
            foreach (var id in f.PageIds) c.Page(id);
        }, new("Business navigation", "Business identity, service area and page navigation", [EditorField.Text("area", "Service area", "Your neighbourhood", 160),
            EditorField.Text("tagline", "Business introduction", "Practical help for your home", 200),
            EditorField.List("pageIds", "Navigation pages", "Navigation page", EditorField.Choice("page", "Navigation page", "", EditorChoiceSource.Pages), 1, 20, true)])),
        new BlockDefinition<ContactFields>(Descriptor("service-contact"), (f, c) =>
        {
            CompositionRules.Text(f.Heading, 160); CompositionRules.Text(f.Text, 1000); CompositionRules.Text(f.Label, 70); c.Destination(f.Destination);
        }, new("Contact panel", "A shared invitation beside every page", [EditorField.Text("heading", "Heading", "Let's talk about your home", 160),
            EditorField.Text("text", "Contact introduction", "Tell us what needs attention", 1000, true), EditorField.Text("label", "Link label", "Get in touch", 70),
            EditorField.Destination("destination", "Contact page")], "heading")),
        new BlockDefinition<IntroFields>(Descriptor("service-intro"), (f, _) => CompositionRules.Text(f.Kicker, 120),
            new("Page introduction", "The page headline with a short introductory label", [EditorField.Text("kicker", "Introductory label", "A little help goes a long way", 120)])),
        FaqContent.Definition
    }));
}
