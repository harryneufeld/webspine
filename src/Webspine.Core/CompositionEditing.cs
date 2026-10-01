using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Webspine.Core.Composition;

public sealed record CompositionLocation(string? PageId, string? RegionId, string? ParentBlockId);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "operation")]
[JsonDerivedType(typeof(CreateBlock), "create")]
[JsonDerivedType(typeof(UpdateBlock), "update")]
[JsonDerivedType(typeof(MovePlacement), "move")]
[JsonDerivedType(typeof(GroupPlacements), "group")]
[JsonDerivedType(typeof(ReferenceShared), "reference")]
[JsonDerivedType(typeof(PromoteShared), "promote")]
[JsonDerivedType(typeof(DetachPlacement), "detach")]
[JsonDerivedType(typeof(DeletePlacement), "delete")]
[JsonDerivedType(typeof(DeleteShared), "deleteShared")]
[JsonDerivedType(typeof(EditCompositionPage), "page")]
[JsonDerivedType(typeof(EditCompositionSettings), "settings")]
public abstract record CompositionEdit;
public sealed record CreateBlock(CompositionLocation Location, int Index, string TypeId, int TypeVersion, JsonElement Fields) : CompositionEdit;
public sealed record UpdateBlock(string BlockId, JsonElement Fields) : CompositionEdit;
public sealed record MovePlacement(string PlacementId, CompositionLocation Location, int Index) : CompositionEdit;
public sealed record GroupPlacements(CompositionLocation Location, ImmutableArray<string> PlacementIds, JsonElement Fields) : CompositionEdit;
public sealed record ReferenceShared(string SharedId, CompositionLocation Location, int Index) : CompositionEdit;
public sealed record PromoteShared(string PlacementId) : CompositionEdit;
public sealed record DetachPlacement(string PlacementId) : CompositionEdit;
public sealed record DeletePlacement(string PlacementId) : CompositionEdit;
public sealed record DeleteShared(string SharedId) : CompositionEdit;
public sealed record EditCompositionPage(string PageId, string Title, string Description) : CompositionEdit;
public sealed record EditCompositionSettings(string Title, string Language) : CompositionEdit;
public sealed record CompositionAuthority(bool ContentWrite, bool SharedWrite, bool SettingsWrite);
public sealed class CompositionPermissionException(string message) : Exception(message);
public sealed class CompositionRevisionException() : Exception("The draft changed. Reload before saving.");

// A provider-independent application boundary. Only typed operations can reach the trusted persistence seam.
public sealed class CompositionEditor(ICompositionDraftPersistence source, CompositionDesign design, BlockRegistry registry)
{
    public async Task<CompositionSnapshot> ApplyAsync(string expectedRevision, CompositionEdit edit, CompositionAuthority authority,
        ImmutableArray<string> acknowledgedPages, CancellationToken ct = default)
    {
        source.CompositionCapabilities.Require(CompositionOperation.Read);
        var snapshot = await source.ReadCompositionAsync(ct);
        if (snapshot.Source != source.Identity) throw new ContentValidationException("The selected CMS returned a different source identity.");
        if (snapshot.Revision != expectedRevision) throw new CompositionRevisionException();
        CompositionContract.Validate(snapshot, design, registry);
        var site = snapshot.Website;
        var owners = new HashSet<BlockOwner>();
        string? promotedPage = null;
        Block Find(string id) => site.Blocks.FirstOrDefault(b => b.Id == id) ?? throw new ContentValidationException("Block does not exist.");
        (CompositionLocation Location, Placement Placement, BlockOwner Owner) Locate(string id)
        {
            foreach (var page in site.Pages)
                foreach (var region in page.Regions)
                    foreach (var p in region.Placements)
                        if (p.Id == id) return (new(page.Id, region.Id, null), p, new(OwnerKind.Page, page.Id));
            foreach (var b in site.Blocks)
                foreach (var p in b.Children)
                    if (p.Id == id) return (new(null, null, b.Id), p, b.Owner);
            throw new ContentValidationException("Placement does not exist.");
        }
        BlockOwner Owner(CompositionLocation l)
        {
            if (l is null) throw new ContentValidationException("A destination is required.");
            if (l.ParentBlockId is not null && l.PageId is null && l.RegionId is null)
            {
                var parent = Find(l.ParentBlockId);
                if (!registry.Resolve(parent.TypeId, parent.TypeVersion).Descriptor.Container) throw new ContentValidationException("Destination is not a Group.");
                return parent.Owner;
            }
            if (l.ParentBlockId is null && l.PageId is not null && l.RegionId is not null &&
                site.Pages.Any(p => p.Id == l.PageId && p.Regions.Any(r => r.Id == l.RegionId))) return new(OwnerKind.Page, l.PageId);
            throw new ContentValidationException("Choose one existing page region or Group destination.");
        }
        ImmutableArray<Placement> Items(CompositionLocation l) => l.ParentBlockId is not null ? Find(l.ParentBlockId).Children : site.Pages.Single(p => p.Id == l.PageId).Regions.Single(r => r.Id == l.RegionId).Placements;
        void Set(CompositionLocation l, ImmutableArray<Placement> items)
        {
            if (l.ParentBlockId is not null) site = site with { Blocks = site.Blocks.Select(b => b.Id == l.ParentBlockId ? b with { Children = items } : b).ToImmutableArray() };
            else site = site with { Pages = site.Pages.Select(p => p.Id == l.PageId ? p with { Regions = p.Regions.Select(r => r.Id == l.RegionId ? r with { Placements = items } : r).ToImmutableArray() } : p).ToImmutableArray() };
        }
        void Insert(CompositionLocation l, int index, Placement p)
        {
            var items = Items(l);
            if (index < 0 || index > items.Length) throw new ContentValidationException("Position is outside the destination.");
            Set(l, items.Insert(index, p));
        }
        HashSet<string> Subtree(string id)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string next) { if (!ids.Add(next)) return; foreach (var p in Find(next).Children.Where(p => p.Kind == TargetKind.Block)) Visit(p.TargetId); }
            Visit(id); return ids;
        }
        string NewId(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");
        CompositionOperation operation;
        void Fields(JsonElement fields) { if (fields.ValueKind != JsonValueKind.Object) throw new ContentValidationException("Registered fields must be an object."); }
        switch (edit)
        {
            case CreateBlock e:
                Fields(e.Fields);
                operation = CompositionOperation.Create; source.CompositionCapabilities.Require(operation, e.TypeId, e.TypeVersion);
                var owner = Owner(e.Location); owners.Add(owner);
                var created = new Block(NewId("block"), owner, e.TypeId, e.TypeVersion, e.Fields.Clone(), []);
                site = site with { Blocks = site.Blocks.Add(created) }; Insert(e.Location, e.Index, new(NewId("placement"), TargetKind.Block, created.Id)); break;
            case UpdateBlock e:
                Fields(e.Fields);
                operation = CompositionOperation.Update; var block = Find(e.BlockId); owners.Add(block.Owner);
                source.CompositionCapabilities.Require(operation, block.TypeId, block.TypeVersion);
                site = site with { Blocks = site.Blocks.Select(b => b.Id == e.BlockId ? b with { Fields = e.Fields.Clone() } : b).ToImmutableArray() }; break;
            case MovePlacement e:
                operation = CompositionOperation.Move; var old = Locate(e.PlacementId); var targetOwner = Owner(e.Location);
                owners.Add(old.Owner); owners.Add(targetOwner);
                if (old.Owner != targetOwner) throw new ContentValidationException("Moves preserve ownership. Use a Shared Block reference or detach a copy across owners.");
                if (old.Placement.Kind == TargetKind.Block && e.Location.ParentBlockId is not null && Subtree(old.Placement.TargetId).Contains(e.Location.ParentBlockId)) throw new ContentValidationException("A Block cannot move into its own subtree.");
                Set(old.Location, Items(old.Location).Remove(old.Placement)); Insert(e.Location, e.Index, old.Placement); break;
            case GroupPlacements e:
                Fields(e.Fields);
                operation = CompositionOperation.Group; source.CompositionCapabilities.Require(operation, "group"); owners.Add(Owner(e.Location));
                var original = Items(e.Location);
                if (e.PlacementIds.IsDefaultOrEmpty || e.PlacementIds.Distinct(StringComparer.Ordinal).Count() != e.PlacementIds.Length || e.PlacementIds.Any(id => !original.Any(p => p.Id == id))) throw new ContentValidationException("Select distinct placements from one destination.");
                var selected = original.Where(p => e.PlacementIds.Contains(p.Id)).ToImmutableArray();
                var grouped = new Block(NewId("block"), Owner(e.Location), "group", 1, e.Fields.Clone(), selected);
                site = site with { Blocks = site.Blocks.Add(grouped) };
                var first = original.IndexOf(selected[0]);
                Set(e.Location, original.Where(p => !e.PlacementIds.Contains(p.Id)).ToImmutableArray().Insert(first, new(NewId("placement"), TargetKind.Block, grouped.Id))); break;
            case ReferenceShared e:
                operation = CompositionOperation.Share; owners.Add(Owner(e.Location));
                if (!site.SharedBlocks.Any(s => s.Id == e.SharedId)) throw new ContentValidationException("Shared Block does not exist.");
                Insert(e.Location, e.Index, new(NewId("placement"), TargetKind.Shared, e.SharedId)); break;
            case PromoteShared e:
                operation = CompositionOperation.Share; var promote = Locate(e.PlacementId); owners.Add(promote.Owner);
                if (promote.Placement.Kind != TargetKind.Block || promote.Owner.Kind != OwnerKind.Page) throw new ContentValidationException("Select a page-owned Block to share.");
                if (!authority.SharedWrite) throw new CompositionPermissionException("Shared content permission is required.");
                promotedPage = promote.Owner.Id;
                var sharedId = NewId("shared"); var subtree = Subtree(promote.Placement.TargetId);
                site = site with { SharedBlocks = site.SharedBlocks.Add(new(sharedId, promote.Placement.TargetId)), Blocks = site.Blocks.Select(b => subtree.Contains(b.Id) ? b with { Owner = new(OwnerKind.Shared, sharedId) } : b).ToImmutableArray() };
                Set(promote.Location, Items(promote.Location).Select(p => p.Id == e.PlacementId ? p with { Kind = TargetKind.Shared, TargetId = sharedId } : p).ToImmutableArray()); break;
            case DetachPlacement e:
                operation = CompositionOperation.Detach; owners.Add(Locate(e.PlacementId).Owner); site = CompositionCopies.DetachShared(snapshot, design, registry, e.PlacementId); break;
            case DeletePlacement e:
                operation = CompositionOperation.Delete; var delete = Locate(e.PlacementId); owners.Add(delete.Owner);
                var removed = delete.Placement.Kind == TargetKind.Block ? Subtree(delete.Placement.TargetId) : [];
                Set(delete.Location, Items(delete.Location).Remove(delete.Placement)); site = site with { Blocks = site.Blocks.Where(b => !removed.Contains(b.Id)).ToImmutableArray() }; break;
            case DeleteShared e:
                operation = CompositionOperation.Delete; owners.Add(new(OwnerKind.Shared, e.SharedId)); site = CompositionCopies.DeleteUnreferencedShared(snapshot, design, registry, e.SharedId); break;
            case EditCompositionPage e:
                operation = CompositionOperation.Update; owners.Add(new(OwnerKind.Page, e.PageId));
                if (!site.Pages.Any(p => p.Id == e.PageId)) throw new ContentValidationException("Page does not exist.");
                site = site with { Pages = site.Pages.Select(p => p.Id == e.PageId ? p with { Title = e.Title, Description = e.Description } : p).ToImmutableArray() }; break;
            case EditCompositionSettings e:
                operation = CompositionOperation.Update;
                if (!authority.SettingsWrite) throw new CompositionPermissionException("Website settings permission is required.");
                site = site with { Title = e.Title, Language = e.Language }; break;
            default: throw new ContentValidationException("Unknown composition operation.");
        }
        source.CompositionCapabilities.Require(operation);
        if (owners.Count > 0 && !authority.ContentWrite) throw new CompositionPermissionException("Content editing permission is required.");
        var sharedOwners = owners.Where(o => o.Kind == OwnerKind.Shared).Select(o => o.Id).ToArray();
        if (sharedOwners.Length > 0 && !authority.SharedWrite) throw new CompositionPermissionException("Shared content permission is required.");
        var affected = promotedPage is null ? AffectedPages(snapshot.Website, sharedOwners) : ImmutableArray.Create(promotedPage);
        if ((sharedOwners.Length > 0 || promotedPage is not null) && (acknowledgedPages.IsDefault || !acknowledgedPages.Order(StringComparer.Ordinal).SequenceEqual(affected)))
            throw new ContentValidationException("Review and acknowledge the affected pages at this revision before changing shared content.");
        CompositionContract.Validate(snapshot with { Website = site }, design, registry);
        return await source.CommitCompositionAsync(expectedRevision, site, design, cancellationToken: ct);
    }

    public static ImmutableArray<string> AffectedPages(CompositionWebsite site, IEnumerable<string> sharedIds)
    {
        var wanted = sharedIds.ToHashSet(StringComparer.Ordinal);
        var blocks = site.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var shared = site.SharedBlocks.ToDictionary(s => s.Id, StringComparer.Ordinal);
        bool Contains(Placement p) => p.Kind == TargetKind.Shared && wanted.Contains(p.TargetId) || blocks[p.Kind == TargetKind.Shared ? shared[p.TargetId].RootBlockId : p.TargetId].Children.Any(Contains);
        return site.Pages.Where(p => p.Regions.SelectMany(r => r.Placements).Any(Contains)).Select(p => p.Id).Order(StringComparer.Ordinal).ToImmutableArray();
    }
}
