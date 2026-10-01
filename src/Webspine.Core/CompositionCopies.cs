using System.Collections.Immutable;

namespace Webspine.Core.Composition;

// Pure graph helpers for trusted application operations; persistence validation and authorization still apply.
public static class CompositionCopies
{
    public static CompositionWebsite DetachShared(CompositionSnapshot snapshot, CompositionDesign design, BlockRegistry registry, string placementId)
    {
        CompositionContract.Validate(snapshot, design, registry);
        var site = snapshot.Website;
        var pages = site.Pages;
        var blocks = site.Blocks;
        var targets = site.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var shared = site.SharedBlocks.ToDictionary(s => s.Id, StringComparer.Ordinal);
        BlockOwner? owner = null;
        Placement? original = null;
        foreach (var page in pages)
            foreach (var region in page.Regions)
                foreach (var placement in region.Placements)
                    if (placement.Id == placementId) { owner = new(OwnerKind.Page, page.Id); original = placement; }
        foreach (var block in blocks)
            foreach (var placement in block.Children)
                if (placement.Id == placementId) { owner = block.Owner; original = placement; }
        if (original is null || original.Kind != TargetKind.Shared || owner is null)
            throw new ContentValidationException("Select a Shared Block reference to detach.");
        var copies = ImmutableArray.CreateBuilder<Block>();
        string Clone(string id)
        {
            var block = targets[id];
            var copyId = "block-" + Guid.NewGuid().ToString("N");
            var children = block.Children.Select(p => new Placement("placement-" + Guid.NewGuid().ToString("N"), TargetKind.Block,
                Clone(p.Kind == TargetKind.Shared ? shared[p.TargetId].RootBlockId : p.TargetId))).ToImmutableArray();
            copies.Add(block with { Id = copyId, Owner = owner, Fields = block.Fields.Clone(), Children = children });
            return copyId;
        }
        var replacement = original with { Kind = TargetKind.Block, TargetId = Clone(shared[original.TargetId].RootBlockId) };
        Placement Replace(Placement p) => p.Id == placementId ? replacement : p;
        var changed = site with
        {
            Blocks = blocks.Select(b => b with { Children = b.Children.Select(Replace).ToImmutableArray() }).Concat(copies).ToImmutableArray(),
            Pages = pages.Select(p => p with { Regions = p.Regions.Select(r => r with { Placements = r.Placements.Select(Replace).ToImmutableArray() }).ToImmutableArray() }).ToImmutableArray()
        };
        CompositionContract.Validate(snapshot with { Website = changed }, design, registry);
        return changed;
    }

    public static CompositionWebsite DeleteUnreferencedShared(CompositionSnapshot snapshot, CompositionDesign design, BlockRegistry registry, string sharedId)
    {
        CompositionContract.Validate(snapshot, design, registry);
        var site = snapshot.Website;
        if (!site.SharedBlocks.Any(s => s.Id == sharedId)) throw new ContentValidationException("Shared Block does not exist.");
        if (site.Pages.SelectMany(p => p.Regions).SelectMany(r => r.Placements).Concat(site.Blocks.SelectMany(b => b.Children))
            .Any(p => p.Kind == TargetKind.Shared && p.TargetId == sharedId)) throw new ContentValidationException("Referenced Shared Blocks cannot be deleted.");
        var changed = site with { SharedBlocks = site.SharedBlocks.Where(s => s.Id != sharedId).ToImmutableArray(),
            Blocks = site.Blocks.Where(b => b.Owner != new BlockOwner(OwnerKind.Shared, sharedId)).ToImmutableArray() };
        CompositionContract.Validate(snapshot with { Website = changed }, design, registry);
        return changed;
    }
}
