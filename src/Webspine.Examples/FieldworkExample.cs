using System.Collections.Immutable;
using System.Text.Json;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Content.Faq;
using Webspine.Designs.Fieldwork;

namespace Webspine.Examples;

public static class FieldworkExample
{
    public static WebsiteStarter Start(string title, bool example)
    {
        var blocks = ImmutableArray.CreateBuilder<Block>();
        Block Add<T>(string id, BlockOwner owner, string type, T fields, ImmutableArray<Placement> children = default)
        {
            var block = new Block(id, owner, type, 1, JsonSerializer.SerializeToElement(fields, CompositionJson.Options), children.IsDefault ? [] : children);
            blocks.Add(block); return block;
        }
        Placement Place(string id) => new("placement-" + id, TargetKind.Block, id);
        var pageInfo = example ? new[] { ("home", "/", "Home", "Small repairs. A home that works well."),
            ("services", "/services/", "Services", "A helpful pair of hands, when you need them."),
            ("approach", "/approach/", "Our approach", "Good work starts with a clear conversation."),
            ("contact", "/contact/", "Contact", "Tell us what needs a little attention.") }
            : [("home", "/", "Home", "Welcome to your website")];
        Add("business-brand", new(OwnerKind.Shared, "business"), "service-brand", new BrandFields(example ? "Home repairs · Westbridge" : "Your neighbourhood",
            example ? "Small jobs. Careful work. A familiar face at the door." : "Practical help for your home", pageInfo.Select(p => p.Item1).ToImmutableArray()));
        Add("business-contact", new(OwnerKind.Shared, "contact-panel"), "service-contact", new ContactFields("One room or a whole list?",
            "Start with what matters most. We'll help you work out the next step.", "Plan a visit", example ? "/contact/" : "/"));
        var pages = ImmutableArray.CreateBuilder<CompositionPage>();
        foreach (var (id, path, name, headline) in pageInfo)
        {
            var owner = new BlockOwner(OwnerKind.Page, id); var placements = ImmutableArray.CreateBuilder<Placement>();
            void Body<T>(string suffix, string type, T fields) { var block = Add(id + "-" + suffix, owner, type, fields); placements.Add(Place(block.Id)); }
            Body("intro", "service-intro", new IntroFields(id == "home" ? "Care for the place you call home" : name));
            if (!example) Body("words", "text", new TextFields("Tell your story", "Add your first words here."));
            else if (id == "home")
            {
                Body("image", "image", new ImageFields("house", "Illustration of a welcoming home with a front porch and garden"));
                var childOwner = owner;
                Add("home-note", childOwner, "text", new TextFields("The small things make a difference.", "A door that closes properly. Shelves that stay level. A room that feels looked after. We take care of the everyday jobs on your list."));
                Add("home-detail", childOwner, "text", new TextFields("A plan before the tools come out.", "We listen, explain the work and agree the next step with you. You always know what we're here to do."));
                Add("home-inner", owner, "group", new GroupFields("stack", "start", "small", 1), [Place("home-note"), Place("home-detail")]);
                Add("home-arrangement", owner, "group", new GroupFields("stack", "start", "medium", 1), [Place("home-inner")]); placements.Add(Place("home-arrangement"));
                Body("services", "cards", new CardsFields("How we can help", [new("Repairs & finishing", "Doors, fittings and the jobs you've been meaning to get to.", null, "/services/"),
                    new("Make a room work better", "Shelving, storage and thoughtful practical improvements.", null, "/services/"), new("A seasonal once-over", "A careful check of the little things around your home.", null, "/services/")]));
                Body("questions", "faq", new FaqFields("A few things you might be wondering", [new("Can we start with one room?", "Of course. We can start with one room and plan the next steps together."),
                    new("What happens at the first visit?", "We look at the work together, discuss your priorities and agree a clear plan before anything begins.")]));
            }
            else if (id == "services")
            {
                Body("list", "cards", new CardsFields("Everyday jobs, done with care", [new("Home repairs", "Adjust doors, repair fittings and finish the details that make a difference.", null, "/contact/"),
                    new("Useful improvements", "Add shelves, assemble furniture and make your space easier to use.", null, "/contact/"), new("Home maintenance", "Keep an eye on the small jobs before they become bigger ones.", null, "/contact/")]));
                Body("invitation", "cta", new CtaFields("Have something else in mind?", "Tell us about the job. We'll be clear about where we can help.", "Ask about your project", "/contact/"));
            }
            else if (id == "approach") Body("words", "text", new TextFields("Listen. Plan. Take care.", "First we listen to what you need. Then we explain the work and agree a plan. We arrive prepared, look after your space and leave it tidy. A useful conversation is always the first step."));
            else Body("words", "text", new TextFields("A few details are all we need.", "Describe the job, the room and what you would like to improve. Bring your questions too. This example website has no contact form or booking service connected."));
            pages.Add(new(id, path, name, headline, [new("brand", [new("placement-brand-" + id, TargetKind.Shared, "business")]),
                new("content", placements.ToImmutable()), new("contact", [new("placement-contact-" + id, TargetKind.Shared, "contact-panel")])]));
        }
        var assets = example ? ImmutableArray.Create(new AssetContent("house", "assets/home-illustration.svg", "image/svg+xml")) : [];
        return new(new("site", title, "en", "service", pages.ToImmutable(), blocks.ToImmutable(),
            [new("business", "business-brand"), new("contact-panel", "business-contact")], assets),
            example ? ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add("assets/home-illustration.svg", StudioExample.Resource("home-illustration.svg"))
                : ImmutableDictionary<string, ImmutableArray<byte>>.Empty);
    }
}
