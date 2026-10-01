using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Webspine.Core.Composition;

public static partial class CompositionRules
{
    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
    public static void Identifier(string? value)
    {
        if (value is null || !IdPattern().IsMatch(value)) Fail("Invalid composition identifier.");
    }
    public static void Text(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) Fail("Invalid or oversized text field.");
    }
    public static void Destination(string value, HashSet<string> pagePaths)
    {
        Text(value, 500);
        if (pagePaths.Contains(value)) return;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || value.Any(char.IsControl))
            Fail("Links require a known page or absolute HTTPS URL.");
    }
    [DoesNotReturn]
    internal static void Fail(string message) => throw new ContentValidationException(message);
}

public static class CompositionContract
{
    public const int Version = 2;
    public const int MaximumGroupDepth = 8;
    public const int MaximumExpandedBlocks = 1000;

    public static void Validate(CompositionSnapshot snapshot, CompositionDesign design, BlockRegistry registry)
    {
        if (snapshot is null || snapshot.ContractVersion != Version || snapshot.Website is null)
            CompositionRules.Fail("Unsupported composition contract or missing website.");
        var site = snapshot.Website;
        if (site.Pages.IsDefaultOrEmpty || site.Pages.Length > 100 || site.Pages.Any(p => p is null) ||
            site.Blocks.IsDefaultOrEmpty || site.Blocks.Length > 10000 || site.Blocks.Any(b => b is null) ||
            site.SharedBlocks.IsDefault || site.SharedBlocks.Length > 1000 || site.SharedBlocks.Any(s => s is null))
            CompositionRules.Fail("Invalid or oversized composition collections.");
        // Reuse v1's mandatory website, asset/path and metadata rules without changing its contract.
        ContentContract.Validate(new(snapshot.Source, snapshot.Revision, new(site.Id, site.Title, site.Language,
            site.Pages.Select(p => new PageContent(p.Id, p.Path, p.Title, p.Description,
                [new TextSection("validation", "Validation", "Validation")])).ToImmutableArray(), site.Assets)));
        ValidateDesign(design, registry);
        if (site.LayoutId != design.Layout.Id) CompositionRules.Fail("Unavailable layout.");
        var pages = site.Pages.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var blocks = Unique(site.Blocks, b => b.Id);
        var shared = Unique(site.SharedBlocks, s => s.Id);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in shared.Values)
        {
            if (!blocks.TryGetValue(definition.RootBlockId, out var root) || root.Owner != new BlockOwner(OwnerKind.Shared, definition.Id) ||
                !roots.Add(root.Id)) CompositionRules.Fail("Invalid or duplicate Shared Block root.");
        }
        var ownedUses = blocks.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var edges = blocks.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var placementIds = new HashSet<string>(StringComparer.Ordinal);
        var context = new BlockValidationContext(site, design);

        Block Target(Placement placement, BlockOwner owner, ImmutableArray<string> allowed)
        {
            if (placement is null) CompositionRules.Fail("Null placement.");
            CompositionRules.Identifier(placement.Id); CompositionRules.Identifier(placement.TargetId);
            if (!placementIds.Add(placement.Id)) CompositionRules.Fail("Duplicate placement ID.");
            Block target;
            if (placement.Kind == TargetKind.Block)
            {
                if (!blocks.TryGetValue(placement.TargetId, out target!)) CompositionRules.Fail("Missing Block target.");
                if (target.Owner != owner || roots.Contains(target.Id)) CompositionRules.Fail("Placement violates ownership; shared roots require references.");
                ownedUses[target.Id]++;
            }
            else if (placement.Kind == TargetKind.Shared)
            {
                if (!shared.TryGetValue(placement.TargetId, out var definition)) CompositionRules.Fail("Missing Shared Block reference.");
                target = blocks[definition.RootBlockId];
            }
            else { CompositionRules.Fail("Invalid placement kind."); return null!; }
            if (!allowed.Contains(target.TypeId)) CompositionRules.Fail("Block type is not permitted at this placement.");
            return target;
        }

        foreach (var block in site.Blocks)
        {
            if (block.Owner is null || !Enum.IsDefined(block.Owner.Kind)) CompositionRules.Fail("Invalid Block owner.");
            CompositionRules.Identifier(block.Owner.Id);
            if ((block.Owner.Kind == OwnerKind.Page && !pages.Contains(block.Owner.Id)) ||
                (block.Owner.Kind == OwnerKind.Shared && !shared.ContainsKey(block.Owner.Id))) CompositionRules.Fail("Missing Block owner.");
            var registration = registry.Resolve(block.TypeId, block.TypeVersion);
            if (block.Children.IsDefault || block.Children.Length > MaximumExpandedBlocks ||
                (!registration.Descriptor.Container && !block.Children.IsEmpty)) CompositionRules.Fail("Invalid Block children.");
            if (block.Fields.ValueKind != JsonValueKind.Object || block.Fields.GetRawText().Length > 65536)
                CompositionRules.Fail("Block fields must be a bounded object.");
            ValidateFieldJson(block.Fields, 0);
            registration.Validate(block, context);
            foreach (var child in block.Children) edges[block.Id].Add(Target(child, block.Owner, design.Groups.AllowedTypes).Id);
        }
        var pageRoots = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var page in site.Pages)
        {
            if (page.Regions.IsDefault || page.Regions.Length != design.Layout.Regions.Length || page.Regions.Any(r => r is null))
                CompositionRules.Fail("Page must fill each declared Region exactly once.");
            var regions = Unique(page.Regions, r => r.Id);
            var list = new List<string>();
            foreach (var definition in design.Layout.Regions)
            {
                if (!regions.TryGetValue(definition.Id, out var region) || region.Placements.IsDefault ||
                    region.Placements.Length < definition.Minimum || region.Placements.Length > definition.Maximum)
                    CompositionRules.Fail("Missing Region or invalid placement count.");
                foreach (var placement in region.Placements)
                    list.Add(Target(placement, new(OwnerKind.Page, page.Id), definition.AllowedTypes).Id);
            }
            pageRoots.Add(page.Id, list);
        }
        foreach (var id in blocks.Keys)
            if (ownedUses[id] != (roots.Contains(id) ? 0 : 1)) CompositionRules.Fail("Block must have exactly one owning placement.");
        RejectCycles(edges);
        // Count expanded occurrences, including repeated shared references, before any renderer runs.
        void Expand(string id, int depth, ref int count)
        {
            if (++count > MaximumExpandedBlocks) CompositionRules.Fail("Expanded Block count exceeds 1,000.");
            var container = registry.Resolve(blocks[id].TypeId, blocks[id].TypeVersion).Descriptor.Container;
            var nextDepth = depth + (container ? 1 : 0);
            if (nextDepth > MaximumGroupDepth) CompositionRules.Fail("Group depth exceeds 8.");
            foreach (var child in edges[id]) Expand(child, nextDepth, ref count);
        }
        foreach (var list in pageRoots.Values)
        {
            var count = 0;
            foreach (var id in list) Expand(id, 0, ref count);
        }
        foreach (var root in roots) { var count = 0; Expand(root, 0, ref count); }
    }

    private static void ValidateDesign(CompositionDesign design, BlockRegistry registry)
    {
        if (design is null || design.Layout is null || design.Groups is null) CompositionRules.Fail("Captured design is required.");
        CompositionRules.Identifier(design.Id); CompositionRules.Text(design.Revision, 256);
        if (design.Stylesheet is null || design.Stylesheet.Length > 1024 * 1024) CompositionRules.Fail("Invalid design stylesheet.");
        CompositionRules.Identifier(design.Layout.Id);
        if (design.Layout.Regions.IsDefaultOrEmpty || design.Layout.Regions.Length > 10 || design.Layout.Regions.Any(r => r is null))
            CompositionRules.Fail("Invalid layout Regions.");
        var regions = Unique(design.Layout.Regions, r => r.Id);
        if (!regions.ContainsKey("header") || !regions.ContainsKey("main") || !regions.ContainsKey("footer"))
            CompositionRules.Fail("The default layout needs Header, Main and Footer.");
        void Types(ImmutableArray<string> types)
        {
            if (types.IsDefaultOrEmpty || types.Length > 100 || types.Distinct(StringComparer.Ordinal).Count() != types.Length)
                CompositionRules.Fail("Invalid permitted type list.");
            foreach (var type in types)
                if (!registry.Descriptors.Any(d => d.Id == type)) CompositionRules.Fail("Design requires an unavailable Block type.");
        }
        foreach (var region in regions.Values)
        {
            Types(region.AllowedTypes);
            if (region.Minimum < 0 || region.Maximum < region.Minimum || region.Maximum > MaximumExpandedBlocks)
                CompositionRules.Fail("Invalid Region limits.");
        }
        Types(design.Groups.AllowedTypes);
        foreach (var choices in new[] { design.Groups.Modes, design.Groups.Alignments, design.Groups.Spacing })
        {
            if (choices.IsDefaultOrEmpty || choices.Length > 20 || choices.Distinct(StringComparer.Ordinal).Count() != choices.Length)
                CompositionRules.Fail("Invalid Group choices.");
            foreach (var choice in choices) CompositionRules.Identifier(choice);
        }
        if (design.Groups.MaximumColumns is < 1 or > 12) CompositionRules.Fail("Invalid maximum grid columns.");
    }

    private static Dictionary<string, T> Unique<T>(IEnumerable<T> values, Func<T, string> id)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            CompositionRules.Identifier(id(value));
            if (!result.TryAdd(id(value), value)) CompositionRules.Fail("Duplicate composition identifier.");
        }
        return result;
    }

    private static void ValidateFieldJson(JsonElement fields, int depth)
    {
        if (depth > 16) CompositionRules.Fail("Block field nesting exceeds 16.");
        if (fields.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in fields.EnumerateObject())
            {
                if (!names.Add(property.Name)) CompositionRules.Fail("Duplicate Block field.");
                ValidateFieldJson(property.Value, depth + 1);
            }
        }
        else if (fields.ValueKind == JsonValueKind.Array)
            foreach (var item in fields.EnumerateArray()) ValidateFieldJson(item, depth + 1);
    }

    private static void RejectCycles(Dictionary<string, List<string>> edges)
    {
        var incoming = edges.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var children in edges.Values) foreach (var child in children) incoming[child]++;
        var queue = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (queue.TryDequeue(out var id))
        {
            visited++;
            foreach (var child in edges[id]) if (--incoming[child] == 0) queue.Enqueue(child);
        }
        if (visited != edges.Count) CompositionRules.Fail("Composition reference cycle.");
    }
}
