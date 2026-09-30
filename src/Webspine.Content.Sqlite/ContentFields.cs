using System.Collections.Immutable;
using Webspine.Core;

namespace Webspine.Content.Sqlite;

public sealed record EditableField(string Key, string Label, string Value, bool Multiline = false);

// The editor and source adapter share one approved field mapping.
public static class ContentFields
{
    public static ImmutableArray<EditableField> Describe(ContentSection section)
    {
        var fields = ImmutableArray.CreateBuilder<EditableField>();
        void Add(string key, string label, string value, bool multiline = false) => fields.Add(new(section.Id + "." + key, label, value, multiline));
        switch (section)
        {
            case TextSection text:
                Add("heading", "Heading", text.Heading); Add("text", "Text", text.Text, true); break;
            case ImageSection image:
                Add("alternativeText", "Image description", image.AlternativeText); break;
            case CtaSection cta:
                Add("heading", "Heading", cta.Heading); Add("text", "Text", cta.Text, true);
                Add("label", "Button label", cta.Label); Add("destination", "Button destination", cta.Destination); break;
            case CardsSection cards:
                Add("heading", "Heading", cards.Heading);
                for (var i = 0; i < cards.Items.Length; i++)
                {
                    var card = cards.Items[i];
                    Add($"items.{i}.title", $"Card {i + 1} title", card.Title);
                    Add($"items.{i}.description", $"Card {i + 1} description", card.Description, true);
                    Add($"items.{i}.destination", $"Card {i + 1} destination", card.Destination);
                }
                break;
            default: throw new ContentValidationException("Unsupported section type.");
        }
        return fields.ToImmutable();
    }
    public static ContentSection Apply(ContentSection section, IReadOnlyDictionary<string, string> values)
    {
        string Value(string key, string fallback) => values.TryGetValue(section.Id + "." + key, out var value) ? value : fallback;
        return section switch
        {
            TextSection text => text with { Heading = Value("heading", text.Heading), Text = Value("text", text.Text) },
            ImageSection image => image with { AlternativeText = Value("alternativeText", image.AlternativeText) },
            CtaSection cta => cta with { Heading = Value("heading", cta.Heading), Text = Value("text", cta.Text), Label = Value("label", cta.Label), Destination = Value("destination", cta.Destination) },
            CardsSection cards => cards with { Heading = Value("heading", cards.Heading), Items = cards.Items.Select((card, i) => card with { Title = Value($"items.{i}.title", card.Title), Description = Value($"items.{i}.description", card.Description), Destination = Value($"items.{i}.destination", card.Destination) }).ToImmutableArray() },
            _ => throw new ContentValidationException("Unsupported section type.")
        };
    }
}
