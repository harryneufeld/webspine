using System.Collections.Immutable;

namespace Webspine.Core.Composition;

public static class StandardContentTypes
{
    // RendererVersion is retained descriptor compatibility metadata; new definitions have no renderer.
    private static BlockTypeDescriptor Descriptor(string id, bool container = false) =>
        new(id, 1, 2, "webspine-standard", "1", id + "-fields-v1", "none", container);
    public static ImmutableArray<IBlockDefinition> Definitions =>
    [
        new BlockDefinition<TextFields>(Descriptor("text"), (v, _) => { CompositionRules.Text(v.Heading, 160); CompositionRules.Text(v.Text, 5000); },
            new("Text", "A heading and your words", [EditorField.Text("heading", "Heading", "New heading", 160), EditorField.Text("text", "Text", "Your text", 5000, true)], "heading")),
        new BlockDefinition<ImageFields>(Descriptor("image"), (v, c) => { c.Asset(v.AssetId); CompositionRules.Text(v.AlternativeText, 300); },
            new("Image", "An approved image with alternative text", [EditorField.Image("assetId", "Image"), EditorField.Text("alternativeText", "Image description", "Describe this image", 300)])),
        new BlockDefinition<CtaFields>(Descriptor("cta"), (v, c) =>
        {
            CompositionRules.Text(v.Heading, 160); CompositionRules.Text(v.Text, 2000); CompositionRules.Text(v.Label, 70); c.Destination(v.Destination);
        }, new("Call to action", "An invitation and a link", [EditorField.Text("heading", "Heading", "Take the next step", 160), EditorField.Text("text", "Invitation", "Your invitation", 2000, true),
            EditorField.Text("label", "Button label", "Learn more", 70), EditorField.Destination("destination", "Link destination")], "heading")),
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
        }, new("Cards", "A list of cards with links and optional images", [EditorField.Text("heading", "Heading", "Explore", 160),
            EditorField.Items("items", "Cards", "Card", [EditorField.Text("title", "Card title", "New card", 160), EditorField.Text("description", "Description", "Your description", 2000, true),
                EditorField.Image("assetId", "Optional image", false), EditorField.Destination("destination", "Link destination")], 1, 50)], "heading")),
        new BlockDefinition<GroupFields>(Descriptor("group", true), (v, c) =>
        {
            if (!c.Design.Groups.Modes.Contains(v.Mode) || !c.Design.Groups.Alignments.Contains(v.Alignment) ||
                !c.Design.Groups.Spacing.Contains(v.Spacing) || v.Columns < 1 || v.Columns > c.Design.Groups.MaximumColumns)
                CompositionRules.Fail("Unapproved Group layout choice.");
        }, new("Group", "Arrange nested elements in a stack, row or grid", [EditorField.Choice("mode", "Layout", "stack", EditorChoiceSource.GroupModes),
            EditorField.Choice("alignment", "Alignment", "start", EditorChoiceSource.GroupAlignments), EditorField.Choice("spacing", "Spacing", "medium", EditorChoiceSource.GroupSpacing),
            EditorField.Number("columns", "Columns", 1, 1, 12, EditorChoiceSource.GroupColumns)]))
    ];
}
