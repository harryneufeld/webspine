using System.Collections.Immutable;

namespace Webspine.Core.Composition;

public static class StandardContentTypes
{
    // RendererVersion is retained descriptor compatibility metadata; new definitions have no renderer.
    private static BlockTypeDescriptor Descriptor(string id, bool container = false) =>
        new(id, 1, 2, "webspine-standard", "1", id + "-fields-v1", "none", container);
    public static ImmutableArray<IBlockDefinition> Definitions =>
    [
        new BlockDefinition<TextFields>(Descriptor("text"), (v, _) => { CompositionRules.Text(v.Heading, 160); CompositionRules.Text(v.Text, 5000); }),
        new BlockDefinition<ImageFields>(Descriptor("image"), (v, c) => { c.Asset(v.AssetId); CompositionRules.Text(v.AlternativeText, 300); }),
        new BlockDefinition<CtaFields>(Descriptor("cta"), (v, c) =>
        {
            CompositionRules.Text(v.Heading, 160); CompositionRules.Text(v.Text, 2000); CompositionRules.Text(v.Label, 70); c.Destination(v.Destination);
        }),
        new BlockDefinition<CardsFields>(Descriptor("cards"), (v, c) =>
        {
            CompositionRules.Text(v.Heading, 160);
            if (v.Items.IsDefaultOrEmpty || v.Items.Length > 50) CompositionRules.Fail("Cards need 1–50 items.");
            foreach (var card in v.Items)
            {
                if (card is null) CompositionRules.Fail("Null card.");
                CompositionRules.Text(card.Title, 160); CompositionRules.Text(card.Description, 2000);
                if (card.AssetId is not null) c.Asset(card.AssetId);
                c.Destination(card.Destination);
            }
        }),
        new BlockDefinition<GroupFields>(Descriptor("group", true), (v, c) =>
        {
            if (!c.Design.Groups.Modes.Contains(v.Mode) || !c.Design.Groups.Alignments.Contains(v.Alignment) ||
                !c.Design.Groups.Spacing.Contains(v.Spacing) || v.Columns < 1 || v.Columns > c.Design.Groups.MaximumColumns)
                CompositionRules.Fail("Unapproved Group layout choice.");
        })
    ];
}
