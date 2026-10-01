using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Content.Faq;

public sealed record FaqItem([property: JsonRequired] string Question, [property: JsonRequired] string Answer);
public sealed record FaqFields([property: JsonRequired] string Heading, [property: JsonRequired] ImmutableArray<FaqItem> Items);

// This content definition has no dependency on any website design or management UI.
public static class FaqContent
{
    public static IBlockDefinition Definition => new BlockDefinition<FaqFields>(
        new("faq", 1, 2, "webspine-faq-content", "1", "faq-fields-v1", "none", false), (fields, _) =>
        {
            CompositionRules.Text(fields.Heading, 160);
            if (fields.Items.IsDefaultOrEmpty || fields.Items.Length > 20) throw new ContentValidationException("FAQ needs 1–20 questions.");
            var questions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in fields.Items)
            {
                if (item is null) throw new ContentValidationException("An FAQ item is required.");
                CompositionRules.Text(item.Question, 200); CompositionRules.Text(item.Answer, 3000);
                if (!questions.Add(item.Question.Trim())) throw new ContentValidationException("Each FAQ question must be distinct.");
            }
        }, new("Questions and answers", "Help visitors find answers to common questions", [EditorField.Text("heading", "Heading", "Frequently asked questions", 160),
            EditorField.Items("items", "Questions", "Question", [EditorField.Text("question", "Question", "Your question", 200),
                EditorField.Text("answer", "Answer", "Your answer", 3000, true)], 1, 20)], "heading"));
}
