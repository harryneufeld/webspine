using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Webspine.Core;

public sealed record SourceCapabilities(bool Read, bool ConditionalDraftWrite, bool SourceWorkflow);
public sealed record SourceIdentity(string Id, string Kind);
public sealed record ContentSnapshot(SourceIdentity Source, string Revision, WebsiteContent Website);
public sealed record WebsiteContent(string Id, string Title, string Language,
    ImmutableArray<PageContent> Pages, ImmutableArray<AssetContent> Assets);
public sealed record PageContent(string Id, string Path, string Title, string Description,
    ImmutableArray<ContentSection> Sections);
public sealed record AssetContent(string Id, string File, string ContentType);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextSection), "text")]
[JsonDerivedType(typeof(ImageSection), "image")]
[JsonDerivedType(typeof(CtaSection), "cta")]
[JsonDerivedType(typeof(CardsSection), "cards")]
public abstract record ContentSection(string Id);
public sealed record TextSection(string Id, string Heading, string Text) : ContentSection(Id);
public sealed record ImageSection(string Id, string AssetId, string AlternativeText) : ContentSection(Id);
public sealed record CtaSection(string Id, string Heading, string Text, string Label, string Destination) : ContentSection(Id);
public sealed record CardContent(string Title, string Description, string? AssetId, string Destination);
public sealed record CardsSection(string Id, string Heading, ImmutableArray<CardContent> Items) : ContentSection(Id);

// Adapter implementations must make conditional writes atomic in their authoritative source.
// The management layer authorizes the caller before invoking an adapter.
public interface IContentSource
{
    SourceIdentity Identity { get; }
    SourceCapabilities Capabilities { get; }
    ValueTask<ContentSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    ValueTask<ContentSnapshot> UpdateDraftAsync(DraftChange change, CancellationToken cancellationToken = default);
}

public sealed record DraftChange(string ExpectedRevision, string PageId, string SectionId,
    string Field, string Value);

public sealed class ContentValidationException(string message) : Exception(message);
public sealed class SourceOperationNotSupportedException(string message) : Exception(message);

public static partial class ContentContract
{
    public const int Version = 1;

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^/(?:[a-z0-9]+(?:-[a-z0-9]+)*/)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PagePathPattern();

    public static void Validate(ContentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Source is null) Fail("A source identity is required.");
        Identifier(snapshot.Source!.Id);
        Text(snapshot.Source.Kind, 80);
        Text(snapshot.Revision, 256);
        var website = snapshot.Website ?? throw new ContentValidationException("Website content is required.");
        Identifier(website.Id);
        Text(website.Title, 120);
        Text(website.Language, 35);
        if (website.Pages.IsDefaultOrEmpty || website.Pages.Length > 100) Fail("A website needs 1–100 pages.");
        if (website.Assets.IsDefault || website.Assets.Length > 500) Fail("Invalid asset collection.");
        var assetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in website.Assets)
        {
            if (asset is null) Fail("Null assets are invalid.");
            Identifier(asset!.Id);
            if (!assetIds.Add(asset.Id)) Fail("Duplicate asset identifier.");
            ArtifactPaths.Validate(asset.File);
            if (!asset.File.StartsWith("assets/", StringComparison.Ordinal)) Fail("Assets must be under assets/.");
            if (asset.ContentType is not ("image/svg+xml" or "image/png" or "image/jpeg" or "image/webp"))
                Fail("Unsupported image type.");
        }
        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        var pagePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in website.Pages)
        {
            if (page is null) Fail("Null pages are invalid.");
            Identifier(page!.Id);
            if (!pageIds.Add(page.Id)) Fail("Duplicate page identifier.");
            if (page.Path is null || !PagePathPattern().IsMatch(page.Path) || page.Path.Length > 200 || !pagePaths.Add(page.Path))
                Fail("Page paths must be unique, canonical paths with a trailing slash.");
            Text(page.Title, 120);
            Text(page.Description, 300);
            if (page.Sections.IsDefaultOrEmpty || page.Sections.Length > 100) Fail("A page needs 1–100 sections.");
            var sectionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in page.Sections)
            {
                if (section is null) Fail("Null sections are invalid.");
                Identifier(section!.Id);
                if (!sectionIds.Add(section.Id)) Fail("Duplicate section identifier within a page.");
                switch (section)
                {
                    case TextSection text:
                        Text(text.Heading, 160); Text(text.Text, 5000); break;
                    case ImageSection image:
                        RequireAsset(assetIds, image.AssetId); Text(image.AlternativeText, 300); break;
                    case CtaSection cta:
                        Text(cta.Heading, 160); Text(cta.Text, 2000); Text(cta.Label, 70); Destination(cta.Destination, pagePaths: null); break;
                    case CardsSection cards:
                        Text(cards.Heading, 160);
                        if (cards.Items.IsDefaultOrEmpty || cards.Items.Length > 50) Fail("A card section needs 1–50 items.");
                        foreach (var card in cards.Items)
                        {
                            if (card is null) Fail("Null cards are invalid.");
                            Text(card!.Title, 160); Text(card.Description, 2000);
                            if (card.AssetId is not null) RequireAsset(assetIds, card.AssetId);
                            Destination(card.Destination, pagePaths: null);
                        }
                        break;
                    default: Fail("Unregistered section type."); break;
                }
            }
        }
        if (!pagePaths.Contains("/")) Fail("A website needs a home page at /.");
        foreach (var page in website.Pages)
            foreach (var section in page.Sections)
                if (section is CtaSection cta) Destination(cta.Destination, pagePaths);
                else if (section is CardsSection cards)
                    foreach (var card in cards.Items) Destination(card.Destination, pagePaths);
    }

    private static void Destination(string value, HashSet<string>? pagePaths)
    {
        Text(value, 500);
        if (PagePathPattern().IsMatch(value))
        {
            if (pagePaths is not null && !pagePaths.Contains(value)) Fail("An internal link references a missing page.");
            return;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0 || value.Any(char.IsControl))
            Fail("Links must point to a known site page or an absolute HTTPS URL.");
    }

    private static void RequireAsset(HashSet<string> assets, string id)
    {
        if (id is null || !assets.Contains(id)) Fail("Image references a missing asset.");
    }

    private static void Identifier(string value)
    {
        if (value is null || !IdentifierPattern().IsMatch(value)) Fail("Invalid content identifier.");
    }

    private static void Text(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) Fail($"Text must contain 1–{maximum} characters.");
    }

    private static void Fail(string message) => throw new ContentValidationException(message);
}
