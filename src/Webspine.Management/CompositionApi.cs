using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Management;

internal sealed record CompositionEditRequest([property: JsonRequired] string ExpectedRevision,
    [property: JsonRequired] CompositionEdit Change, ImmutableArray<string> AcknowledgedPages);
internal static class CompositionApi
{
    public static int ErrorStatus(Exception e) => e switch
    {
        CompositionPermissionException => 403, ContentValidationException or JsonException or OverflowException => 422,
        SourceOperationNotSupportedException => 501, _ => 409
    };
    public static bool ExpectedError(Exception e) => e is CompositionPermissionException or ContentValidationException or JsonException or OverflowException or SourceOperationNotSupportedException or CompositionRevisionException or RevisionConflictException or CompositionSiteException or SiteNotInitializedException;
    public static void MapCompositionApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v2");
        api.AddEndpointFilter(async (invocation, next) =>
        {
            try { return await next(invocation); }
            catch (Exception e) when (ExpectedError(e)) { return Results.Problem(e.Message, statusCode: ErrorStatus(e)); }
        });
        api.MapGet("/site", async (CompositionOperations operations, HttpContext c) => Results.Json(await operations.ReadAsync(c.RequestAborted), CompositionJson.Options)).WithMetadata(new ApiPermission("content:read"));
        api.MapGet("/capabilities", (CompositionOperations operations) => Results.Json(operations.Source.CompositionCapabilities, CompositionJson.Options)).WithMetadata(new ApiPermission("content:read"));
        api.MapGet("/schema", async (CompositionOperations operations, HttpContext c) =>
            Results.Json(await operations.SchemaAsync(c.User, c.RequestAborted), CompositionJson.Options)).WithMetadata(new ApiPermission("content:read"));
        api.MapGet("/shared/{id}/impact", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted);
            if (!snapshot.Website.SharedBlocks.Any(s => s.Id == id)) return Results.NotFound();
            return Results.Json(new { snapshot.Revision, affectedPages = CompositionEditor.AffectedPages(snapshot.Website, [id]) }, CompositionJson.Options);
        }).WithMetadata(new ApiPermission("content:read"));
        api.MapPost("/changes", async (HttpContext c, CompositionOperations operations) =>
        {
            using var json = await JsonDocument.ParseAsync(c.Request.Body, new JsonDocumentOptions { MaxDepth = 32 }, c.RequestAborted);
            CompositionJson.RejectDuplicateProperties(json.RootElement);
            var request = JsonSerializer.Deserialize<CompositionEditRequest>(json.RootElement.GetRawText(), CompositionJson.Options) ?? throw new ContentValidationException("An operation is required.");
            if (request.Change is null) throw new ContentValidationException("An operation is required.");
            return Results.Json(await operations.EditAsync(request.ExpectedRevision, request.Change, c.User, request.AcknowledgedPages, c.RequestAborted), CompositionJson.Options);
        }).WithMetadata(new ApiPermission("content:read")); // Each typed operation authorizes its own write permission.
        api.MapPost("/media", async (HttpContext c, CompositionOperations operations) =>
        {
            if (c.Request.ContentType != "image/png") return Results.Problem("Use image/png with raw image bytes and an If-Match revision header.", statusCode: 415);
            var revision = c.Request.Headers.IfMatch.ToString().Trim('"');
            var bytes = await ReadUpload(c.Request.Body, c.RequestAborted);
            return Results.Json(await operations.MediaAsync(revision, bytes, c.User, c.RequestAborted), CompositionJson.Options);
        }).WithMetadata(new ApiPermission("content:write"));
        api.MapPost("/previews", async (PreviewRequest request, CompositionOperations operations, HttpContext c) =>
        {
            var preview = await operations.PreviewAsync(request.ExpectedRevision, "/api/v2/previews/", c.RequestAborted);
            return Results.Created("/api/v2/previews/" + preview.Id + "/", new { preview.Id, preview.Artifact.Digest, url = "/api/v2/previews/" + preview.Id + "/" });
        }).WithMetadata(new ApiPermission("preview:build"));
    }
    public static async Task<byte[]> ReadUpload(Stream input, CancellationToken ct)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + count > CompositionMedia.MaximumBytes) throw new ContentValidationException("Image exceeds the 2 MiB upload limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
