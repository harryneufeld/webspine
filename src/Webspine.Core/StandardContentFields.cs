using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Webspine.Core.Composition;

public sealed record TextFields([property: JsonRequired] string Heading, [property: JsonRequired] string Text);
public sealed record ImageFields([property: JsonRequired] string AssetId, [property: JsonRequired] string AlternativeText);
public sealed record CtaFields([property: JsonRequired] string Heading, [property: JsonRequired] string Text,
    [property: JsonRequired] string Label, [property: JsonRequired] string Destination);
public sealed record CardFields([property: JsonRequired] string Title, [property: JsonRequired] string Description,
    string? AssetId, [property: JsonRequired] string Destination);
public sealed record CardsFields([property: JsonRequired] string Heading, [property: JsonRequired] ImmutableArray<CardFields> Items);
public sealed record GroupFields([property: JsonRequired] string Mode, [property: JsonRequired] string Alignment,
    [property: JsonRequired] string Spacing, [property: JsonRequired] int Columns);
