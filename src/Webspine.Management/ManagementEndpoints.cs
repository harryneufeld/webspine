using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Webspine.Caching.Memory;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Delivery;
using Webspine.Examples;

namespace Webspine.Management;

internal static class ManagementEndpoints
{
    public static async Task MapManagementAsync(this WebApplication app)
    {
        var store = app.Services.GetRequiredService<SqliteContentSource>();
        await store.InitializeSchemaAsync();
        app.Use(async (context, next) =>
        {
            var isApi = context.Request.Path.StartsWithSegments("/api");
            if (context.Request.Path.StartsWithSegments("/manage") || isApi)
            {
                var host = context.Request.Host.Host;
                if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
                    !(host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1" or "[::1]"))
                { context.Response.StatusCode = 403; return; }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = context.Request.Path.Value is "/api/v2/media" or "/manage/composition/media" ? 2 * 1024 * 1024 + 65536 : 262144;
                var path = context.Request.Path.Value ?? "";
                if (isApi)
                {
                    var identity = await context.AuthenticateAsync(IntegrationAuthentication.SchemeName);
                    if (!identity.Succeeded) { context.Response.Headers.WWWAuthenticate = "Bearer"; context.Response.StatusCode = 401; return; }
                    context.User = identity.Principal!;
                }
                var publicAccount = path is "/manage/account/start" or "/manage/account/login" or "/manage/assets/editor.css";
                if (!publicAccount)
                {
                    if (context.User.Identity?.IsAuthenticated != true) { context.Response.Redirect("/manage/account/login"); return; }
                    var permission = context.GetEndpoint()?.Metadata.GetMetadata<ApiPermission>()?.Name ?? (isApi ? "content:read"
                        : path.StartsWith("/manage/integrations", StringComparison.Ordinal) ? "integrations:manage"
                        : path.StartsWith("/manage/users", StringComparison.Ordinal) ? "accounts:manage"
                        : path == "/manage/setup" ? "settings:write"
                        : path.StartsWith("/manage/pages/", StringComparison.Ordinal) ? "content:write"
                        : path.StartsWith("/manage/settings", StringComparison.Ordinal) ? "settings:write"
                        : path.StartsWith("/manage/preview/", StringComparison.Ordinal) ? "preview:read"
                        : path == "/manage/preview" ? "preview:build"
                        : path == "/manage/composition/settings" ? "settings:write"
                        : HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || path is "/manage/account/logout" or "/manage/account/password" ? "content:read" : "content:write");
                    if (!Permissions.Has(context.User, permission)) { context.Response.StatusCode = 403; return; }
                }
                if (!isApi && HttpMethods.IsPost(context.Request.Method))
                {
                    try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                    catch (AntiforgeryValidationException) { await ManagementUi.Problem("This form expired or could not be verified. Reopen the page and try again.", 400).ExecuteAsync(context); return; }
                }
            }
            await next(context);
        });
        app.MapGet("/manage/assets/editor.css", () => Results.Text(ManagementUi.Css + CompositionBoard.Css, "text/css; charset=utf-8"));
        app.MapGet("/manage", (Delegate)CompositionBoard.HomeAsync);
        app.MapPost("/manage/setup", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            try
            {
                var title = form["title"].ToString();
                if (form["mode"] != "demo" && form["mode"] != "blank") throw new ContentValidationException("Choose Start blank or the installed example.");
                var installation = context.RequestServices.GetRequiredService<InstalledDesign>();
                var starter = installation.Start(title, form["mode"] == "demo");
                if (!Permissions.Has(context.User, "content:write") || (!starter.Composition.SharedBlocks.IsEmpty && !Permissions.Has(context.User, "content:shared:write")))
                    return ManagementUi.Problem("Creating this site requires content and shared-content editing permission.", 403);
                await store.CreateCompositionAsync(starter.Composition, installation.Package.Design, starter.Assets, context.RequestAborted);
                return Results.Redirect("/manage");
            }
            catch (SiteAlreadyExistsException error) { return ManagementUi.Problem(error.Message, 409); }
            catch (ContentValidationException error) { return ManagementUi.Setup(context, error.Message, 422, form["title"].ToString()); }
        });
        app.MapMethods("/manage/preview/{id}/{**path}", ["GET", "HEAD"], async (string id, string? path, HttpContext context) =>
        {
            var artifact = await store.ReadPreviewAsync(id, context.RequestAborted);
            if (artifact is null) { context.Response.StatusCode = 404; return; }
            await new PrerenderedDelivery(new FixedArtifactSource(artifact), new NoDeliveryCache(), scripts: DesignScriptPolicy.FromArtifact(artifact)).DeliverAsync(context, path);
        });
        foreach (var route in new[] { "/manage/pages", "/manage/pages/{**path}", "/manage/settings", "/manage/export", "/manage/preview", "/manage/upgrade-composition" })
            app.MapMethods(route, ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"], () =>
                ManagementUi.Problem("This legacy editing operation has been removed. Open your website to use composition editing. Older v1 workspaces require a fresh v2 setup in a separate data directory.", 410));
    }
}
