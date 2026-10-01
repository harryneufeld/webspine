using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using Webspine.Core;

namespace Webspine.Rendering.Legacy;

public sealed class LegacyWebsiteRenderer(string stylesheet, ImmutableDictionary<string, ImmutableArray<byte>> assets,
    string basePath = "") : IWebsiteRenderer
{
    public ValueTask RenderAsync(BuildInputs inputs, ArtifactBuilder output, CancellationToken cancellationToken = default)
    {
        output.AddText("assets/site.css", stylesheet);
        foreach (var asset in inputs.Content.Website.Assets)
        {
            if (!assets.TryGetValue(asset.File, out var bytes)) throw new ContentValidationException("A demo asset is missing.");
            output.Add(asset.File, bytes.AsSpan());
        }
        var website = inputs.Content.Website;
        foreach (var page in website.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var html = new StringBuilder();
            html.Append($"<!doctype html><html lang=\"{E(website.Language)}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{E(page.Title)} · {E(website.Title)}</title><meta name=\"description\" content=\"{E(page.Description)}\"><link rel=\"stylesheet\" href=\"{E(basePath)}/assets/site.css\"></head><body>");
            html.Append($"<a class=\"skip\" href=\"#main\">Skip to content</a><header class=\"site-container\"><a class=\"brand\" href=\"{Link("/")}\">{E(website.Title)}<span>Independent studio</span></a><nav aria-label=\"Main navigation\">");
            foreach (var item in website.Pages)
                html.Append($"<a href=\"{Link(item.Path)}\"{(item.Id == page.Id ? " aria-current=\"page\"" : "")}>{E(item.Title)}</a>");
            html.Append($"</nav></header><main id=\"main\" class=\"site-container\"><div class=\"page-title\"><p class=\"eyebrow\">{E(website.Title)} / {E(page.Title)}</p><h1>{E(page.Description)}</h1></div>");
            foreach (var section in page.Sections)
            {
                html.Append($"<section id=\"{E(section.Id)}\" class=\"section\">");
                switch (section)
                {
                    case TextSection text:
                        html.Append($"<h2>{E(text.Heading)}</h2><p class=\"body-copy\">{E(text.Text)}</p>"); break;
                    case ImageSection image:
                        var asset = website.Assets.Single(a => a.Id == image.AssetId);
                        html.Append($"<img class=\"wide-image\" src=\"{E(basePath)}/{E(asset.File)}\" alt=\"{E(image.AlternativeText)}\" width=\"1000\" height=\"460\">"); break;
                    case CtaSection cta:
                        html.Append($"<div class=\"cta\"><div><h2>{E(cta.Heading)}</h2><p>{E(cta.Text)}</p></div><a class=\"button\" href=\"{Link(cta.Destination)}\">{E(cta.Label)} <span aria-hidden=\"true\">↗</span></a></div>"); break;
                    case CardsSection cards:
                        html.Append($"<h2>{E(cards.Heading)}</h2><div class=\"cards\">");
                        foreach (var card in cards.Items)
                        {
                            html.Append("<article class=\"card\">");
                            if (card.AssetId is not null)
                            {
                                var cardAsset = website.Assets.Single(a => a.Id == card.AssetId);
                                html.Append($"<img src=\"{E(basePath)}/{E(cardAsset.File)}\" alt=\"\" width=\"1000\" height=\"460\">");
                            }
                            html.Append($"<h3>{E(card.Title)}</h3><p>{E(card.Description)}</p><a href=\"{Link(card.Destination)}\">Learn more <span aria-hidden=\"true\">↗</span></a></article>");
                        }
                        html.Append("</div>"); break;
                    default: throw new ContentValidationException("The demo renderer does not support this section.");
                }
                html.Append("</section>");
            }
            var footer = inputs.Content.Source.Kind == "fixture" ? "Example website · Fictional business · No transactions or contact submissions" : "Draft preview · Not published";
            html.Append($"</main><footer class=\"site-container\"><span>{E(website.Title)}</span><p>{E(footer)}</p></footer></body></html>");
            output.AddText(BuildPipeline.PageFile(page.Path), html.ToString());
        }
        return ValueTask.CompletedTask;
    }

    private string Link(string destination) => E(destination.StartsWith('/') ? basePath + destination : destination);
    private static string E(string value) => WebUtility.HtmlEncode(value);
}

public sealed class LegacySiteIndexContributor : IArtifactContributor
{
    public string Id => "demo.site-index";
    public ValueTask ContributeAsync(BuildInputs inputs, ArtifactBuilder output, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        output.AddText("site-index.json", JsonSerializer.Serialize(new
        {
            contractVersion = ContentContract.Version, source = inputs.Content.Source,
            sourceRevision = inputs.Content.Revision,
            pages = inputs.Content.Website.Pages.Select(page => new { page.Id, page.Path, page.Title })
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return ValueTask.CompletedTask;
    }
}
