using Webspine.Content.Sqlite;
using Webspine.Delivery;
using Webspine.Caching.Memory;

namespace Webspine.Management;

internal sealed record ApiPermission(string Name);
internal sealed record PreviewRequest(string ExpectedRevision);

internal static class RetainedPreviews
{
    public static void MapRetainedPreviews(this WebApplication app)
    {
        // A retained artifact is read without loading or rendering the editable source.
        async Task Deliver(string id, string? path, SqliteContentSource store, HttpContext context)
        {
            var artifact = await store.ReadPreviewAsync(id, context.RequestAborted);
            if (artifact is null) { context.Response.StatusCode = 404; return; }
            await new PrerenderedDelivery(new FixedArtifactSource(artifact), new NoDeliveryCache(), scripts: DesignScriptPolicy.FromArtifact(artifact)).DeliverAsync(context, path);
        }
        app.MapMethods("/api/v1/previews/{id}/{**path}", ["GET", "HEAD"], Deliver).WithMetadata(new ApiPermission("preview:read"));
        app.MapMethods("/api/v2/previews/{id}/{**path}", ["GET", "HEAD"], Deliver).WithMetadata(new ApiPermission("preview:read"));
        app.MapMethods("/api/v1/{**path}", ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"], () =>
            Results.Problem("The legacy authoring API has been removed. Use /api/v2/site, /api/v2/schema and /api/v2/changes. Existing preview URLs remain readable with preview:read permission.", statusCode: 410))
            .WithMetadata(new ApiPermission("content:read"));
    }
}
