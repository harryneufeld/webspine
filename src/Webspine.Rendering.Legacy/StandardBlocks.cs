using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Webspine.Core.Composition;

public static class StandardBlocks
{
    private static BlockTypeDescriptor Descriptor(string id, bool container = false) =>
        new(id, 1, CompositionContract.Version, "webspine-standard", "1", id + "-fields-v1", "1", container);

    public static ImmutableArray<IBlockRegistration> Registrations =>
    [
        new BlockRegistration<TextFields>(Descriptor("text"), (value, _) =>
        {
            CompositionRules.Text(value.Heading, 160); CompositionRules.Text(value.Text, 5000);
        }, (value, context, html) =>
        {
            html.Markup("<section class=\"section\"><h2>"); html.Text(value.Heading); html.Markup("</h2><p class=\"body-copy\">"); html.Text(value.Text); html.Markup("</p></section>");
        }),
        new BlockRegistration<ImageFields>(Descriptor("image"), (value, context) =>
        {
            context.Asset(value.AssetId); CompositionRules.Text(value.AlternativeText, 300);
        }, (value, context, html) => Image(value.AssetId, value.AlternativeText, context, html)),
        new BlockRegistration<CtaFields>(Descriptor("cta"), (value, context) =>
        {
            CompositionRules.Text(value.Heading, 160); CompositionRules.Text(value.Text, 2000);
            CompositionRules.Text(value.Label, 70); context.Destination(value.Destination);
        }, (value, context, html) =>
        {
            html.Markup("<section class=\"section cta\"><div><h2>"); html.Text(value.Heading); html.Markup("</h2><p>"); html.Text(value.Text);
            html.Markup("</p></div><a class=\"button\" href=\""); html.Text(context.Link(value.Destination)); html.Markup("\">"); html.Text(value.Label); html.Markup("</a></section>");
        }),
        new BlockRegistration<CardsFields>(Descriptor("cards"), (value, context) =>
        {
            CompositionRules.Text(value.Heading, 160);
            if (value.Items.IsDefaultOrEmpty || value.Items.Length > 50) throw new ContentValidationException("Cards need 1–50 items.");
            foreach (var card in value.Items)
            {
                if (card is null) throw new ContentValidationException("Null card.");
                CompositionRules.Text(card.Title, 160); CompositionRules.Text(card.Description, 2000);
                if (card.AssetId is not null) context.Asset(card.AssetId);
                context.Destination(card.Destination);
            }
        }, (value, context, html) =>
        {
            html.Markup("<section class=\"section\"><h2>"); html.Text(value.Heading); html.Markup("</h2><div class=\"cards\">");
            foreach (var card in value.Items)
            {
                html.Markup("<article class=\"card\">");
                if (card.AssetId is not null) Image(card.AssetId, card.Title, context, html);
                html.Markup("<h3><a href=\""); html.Text(context.Link(card.Destination)); html.Markup("\">"); html.Text(card.Title);
                html.Markup("</a></h3><p>"); html.Text(card.Description); html.Markup("</p></article>");
            }
            html.Markup("</div></section>");
        }),
        new BlockRegistration<GroupFields>(Descriptor("group", container: true), (value, context) =>
        {
            var rules = context.Design.Groups;
            if (!rules.Modes.Contains(value.Mode) || !rules.Alignments.Contains(value.Alignment) || !rules.Spacing.Contains(value.Spacing) ||
                value.Columns < 1 || value.Columns > rules.MaximumColumns) throw new ContentValidationException("Unapproved Group layout choice.");
        }, (value, context, html) =>
        {
            html.Markup("<div class=\"group mode-"); html.Text(value.Mode); html.Markup(" align-"); html.Text(value.Alignment);
            html.Markup(" spacing-"); html.Text(value.Spacing); html.Markup(" columns-"); html.Text(value.Columns.ToString(CultureInfo.InvariantCulture));
            html.Markup("\">"); context.RenderChildren(); html.Markup("</div>");
        })
    ];

    private static void Image(string assetId, string alternativeText, BlockRenderContext context, HtmlOutput html)
    {
        html.Markup("<img class=\"wide-image\" src=\""); html.Text(context.AssetPath(assetId)); html.Markup("\" alt=\""); html.Text(alternativeText); html.Markup("\">");
    }
}
