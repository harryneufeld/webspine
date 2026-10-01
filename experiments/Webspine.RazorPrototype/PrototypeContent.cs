using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.RazorPrototype;

public sealed record BrandFields([property: JsonRequired] string Name, [property: JsonRequired] string Note);
public sealed record FaqItem([property: JsonRequired] string Question, [property: JsonRequired] string Answer);
public sealed record FaqFields([property: JsonRequired] string Heading, [property: JsonRequired] ImmutableArray<FaqItem> Items);

public static class PrototypeContent
{
    // Temporary validator registry reuse; the future package API separates rendering (#25).
    public static BlockRegistry Registry() => new(StandardBlocks.Registrations.AddRange(new IBlockRegistration[] {
        new BlockRegistration<BrandFields>(new("brand", 1, 2, "razor-proof", "1", "brand-v1", "1", false),
            (fields, _) => { CompositionRules.Text(fields.Name, 120); CompositionRules.Text(fields.Note, 200); },
            (_, _, _) => throw new NotSupportedException("Use the Razor component renderer.")),
        new BlockRegistration<FaqFields>(new("faq", 1, 2, "razor-proof", "1", "faq-v1", "1", false),
            (fields, _) =>
            {
                CompositionRules.Text(fields.Heading, 160);
                if (fields.Items.IsDefaultOrEmpty || fields.Items.Length > 20)
                    throw new ContentValidationException("FAQ needs 1–20 questions.");
                foreach (var item in fields.Items)
                {
                    if (item is null) throw new ContentValidationException("Null FAQ item.");
                    CompositionRules.Text(item.Question, 200); CompositionRules.Text(item.Answer, 2000);
                }
            }, (_, _, _) => throw new NotSupportedException("Use the Razor component renderer."))
    }));

    public static CompositionSnapshot Fixture()
    {
        var pageOwner = new BlockOwner(OwnerKind.Page, "home");
        Block Node<T>(string id, string type, T fields, ImmutableArray<Placement> children = default,
            BlockOwner? owner = null) => new(id, owner ?? pageOwner, type, 1,
                JsonSerializer.SerializeToElement(fields, CompositionJson.Options), children.IsDefault ? [] : children);
        Placement Place(string id) => new("place-" + id, TargetKind.Block, id);
        var page = new CompositionPage("home", "/", "Spaces for everyday life",
            "Thoughtful interiors, clear decisions and a place to feel at home.",
            [new("header", [new("header-brand", TargetKind.Shared, "studio-brand")]),
             new("main", [Place("hero"), Place("approach"), Place("questions"), Place("invitation")]),
             new("footer", [new("footer-brand", TargetKind.Shared, "studio-brand")])]);
        var website = new CompositionWebsite("razor-proof", "Alder Studio", "en", "studio", [page],
            [Node("branding", "brand", new BrandFields("Alder Studio", "Interiors made for living."),
                owner: new(OwnerKind.Shared, "studio-brand")),
             Node("hero", "group", new GroupFields("grid", "center", "medium", 2), [Place("intro"), Place("room")]),
             Node("intro", "text", new TextFields("A little more room to be yourself.",
                "We design warm, useful spaces around the way you live. From the first conversation to the final detail, every decision has a purpose.")),
             Node("room", "image", new ImageFields("room-image", "Illustration of a calm room with a lounge chair, window and plant.")),
             Node("approach", "group", new GroupFields("stack", "start", "medium", 1), [Place("approach-title"), Place("principles")]),
             Node("approach-title", "text", new TextFields("Good spaces start with listening.", "A clear process leaves more room for good ideas.")),
             Node("principles", "group", new GroupFields("grid", "start", "medium", 2), [Place("listen"), Place("make")]),
             Node("listen", "text", new TextFields("01 / Understand", "Tell us what works, what does not, and what you want your space to feel like.")),
             Node("make", "text", new TextFields("02 / Make it yours", "Explore a considered plan, materials and details, with time to make confident choices.")),
             Node("questions", "faq", new FaqFields("Before we begin", [
                 new("Can we start with just one room?", "Yes. A small, focused project is often the best place to begin."),
                 new("How do we decide on a budget?", "We agree on the scope and priorities together before design work begins.")])),
             Node("invitation", "cta", new CtaFields("Make space for something better.",
                "Take a look at our process, or start with a conversation.", "Back to the beginning", "/"))],
            [new("studio-brand", "branding")], [new("room-image", "assets/room.svg", "image/svg+xml")]);
        var revision = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(website, CompositionJson.Options));
        return new(2, new("razor-fixture", "fixture"), revision, website);
    }
}
