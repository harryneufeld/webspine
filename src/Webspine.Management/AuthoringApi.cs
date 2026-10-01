using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Delivery;
using Webspine.Caching.Memory;

namespace Webspine.Management;

internal sealed record ApiPermission(string Name);
internal sealed record WebsiteSettingsRequest(string ExpectedRevision, string Title, string Language);
internal sealed record AddPageRequest(string ExpectedRevision, string Title, string Path, string Description);
internal sealed record EditPageRequest(string ExpectedRevision, string Title, string Description, Dictionary<string, string> Fields);
internal sealed record PreviewRequest(string ExpectedRevision);

internal static class AuthoringApi
{
    public static void MapAuthoringApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.AddEndpointFilter(async (invocation, next) =>
        {
            try
            {
                var result = await next(invocation);
                if (!HttpMethods.IsGet(invocation.HttpContext.Request.Method) && !HttpMethods.IsHead(invocation.HttpContext.Request.Method))
                {
                    var context = invocation.HttpContext;
                    app.Logger.LogInformation("Authoring operation {Operation} by credential {Credential} for account {Account}", context.Request.Path,
                        context.User.FindFirst("webspine:credential")?.Value, context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
                }
                return result;
            }
            catch (RevisionConflictException error) { return Results.Problem(error.Message, statusCode: 409); }
            catch (ContentValidationException error) { return Results.Problem(error.Message, statusCode: 422); }
            catch (SiteNotInitializedException error) { return Results.Problem(error.Message, statusCode: 409); }
            catch (CompositionSiteException error) { return Results.Problem(error.Message, statusCode: 409); }
            catch (SourceOperationNotSupportedException error) { return Results.Problem(error.Message, statusCode: 501); }
        });
        api.MapGet("/site", async (AuthoringOperations operations, HttpContext context) => Results.Ok(await operations.ReadAsync(context.RequestAborted)))
            .WithMetadata(new ApiPermission("content:read"));
        api.MapPut("/site", async (WebsiteSettingsRequest request, AuthoringOperations operations, HttpContext context) =>
            Results.Ok(await operations.SettingsAsync(request.ExpectedRevision, request.Title, request.Language, context.RequestAborted)))
            .WithMetadata(new ApiPermission("settings:write"));
        api.MapPost("/pages", async (AddPageRequest request, AuthoringOperations operations, HttpContext context) =>
            Results.Ok(await operations.AddPageAsync(request.ExpectedRevision, request.Title, request.Path, request.Description, context.RequestAborted)))
            .WithMetadata(new ApiPermission("content:write"));
        api.MapPut("/pages/{id}", async (string id, EditPageRequest request, AuthoringOperations operations, HttpContext context) =>
        {
            if (request.Fields is null) return Results.Problem("Fields must be an object of approved field names and values.", statusCode: 422);
            return Results.Ok(await operations.EditPageAsync(request.ExpectedRevision, id, request.Title, request.Description, request.Fields, context.RequestAborted));
        }).WithMetadata(new ApiPermission("content:write"));
        api.MapPost("/previews", async (PreviewRequest request, AuthoringOperations operations, HttpContext context) =>
        {
            var preview = await operations.PreviewAsync(request.ExpectedRevision, "/api/v1/previews/", context.RequestAborted);
            return Results.Created("/api/v1/previews/" + preview.Id + "/", new { preview.Id, preview.Artifact.Digest, preview.Artifact.SourceRevision, url = "/api/v1/previews/" + preview.Id + "/" });
        }).WithMetadata(new ApiPermission("preview:build"));
        api.MapMethods("/previews/{id}/{**path}", ["GET", "HEAD"], async (string id, string? path, SqliteContentSource store, HttpContext context) =>
        {
            var artifact = await store.ReadPreviewAsync(id, context.RequestAborted);
            if (artifact is null) { context.Response.StatusCode = 404; return; }
            await new PrerenderedDelivery(new FixedArtifactSource(artifact), new NoDeliveryCache()).DeliverAsync(context, path);
        }).WithMetadata(new ApiPermission("preview:read"));
    }
}
