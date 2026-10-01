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
using Webspine.Demo;

namespace Webspine.Management;

internal static class ManagementEndpoints
{
    public static async Task MapManagementAsync(this WebApplication app)
    {
        var store = app.Services.GetRequiredService<SqliteContentSource>();
        var operations = app.Services.GetRequiredService<AuthoringOperations>();
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
            var legacyPath = context.Request.Path.Value ?? "";
            if ((legacyPath is "/manage" or "/manage/settings" or "/manage/export" or "/manage/pages" or "/manage/preview" || legacyPath.StartsWith("/manage/pages/", StringComparison.Ordinal)) && (await store.HeadAsync(context.RequestAborted))?.Version == 2)
            {
                if (HttpMethods.IsGet(context.Request.Method)) context.Response.Redirect("/manage/composition");
                else await ManagementUi.Problem("This site uses composition. Reopen the composition board before editing.", 409).ExecuteAsync(context);
                return;
            }
            await next(context);
        });
        app.MapGet("/manage/assets/editor.css", () => Results.Text(ManagementUi.Css, "text/css; charset=utf-8"));
        app.MapGet("/manage", async (HttpContext context) =>
        {
            var snapshot = await store.TryReadAsync(context.RequestAborted);
            if (snapshot is null && !Permissions.Has(context.User, "settings:write")) return ManagementUi.Problem("An operator must create the website first.", 403);
            return snapshot is null ? ManagementUi.Setup(context) : ManagementUi.Overview(context, snapshot, (await store.HistoryAsync(context.RequestAborted)).Length);
        });
        app.MapPost("/manage/setup", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            try
            {
                WebsiteContent website;
                var assets = ImmutableDictionary<string, ImmutableArray<byte>>.Empty;
                var title = form["title"].ToString();
                if (form["mode"] == "demo")
                {
                    var source = new DemoContentSource(await File.ReadAllTextAsync(Path.Combine(DemoSite.FixtureDirectory, "site.json"), context.RequestAborted));
                    website = (await source.ReadAsync(context.RequestAborted)).Website with { Id = "site", Title = title };
                    foreach (var asset in website.Assets)
                        assets = assets.Add(asset.File, (await File.ReadAllBytesAsync(Path.Combine(DemoSite.FixtureDirectory, asset.File), context.RequestAborted)).ToImmutableArray());
                }
                else if (form["mode"] == "blank")
                    website = new("site", title, "en", [new("home", "/", "Home", "Welcome to your website", [new TextSection("introduction", "Tell your story", "Add your first words here.")])], []);
                else throw new ContentValidationException("Choose Start blank or Use demo.");
                await store.CreateAsync(website, assets, context.RequestAborted);
                return Results.Redirect("/manage");
            }
            catch (SiteAlreadyExistsException error) { return ManagementUi.Problem(error.Message, 409); }
            catch (ContentValidationException error) { return ManagementUi.Setup(context, error.Message, 422, form["title"].ToString()); }
        });
        app.MapGet("/manage/pages/{id}", async (string id, HttpContext context) =>
        {
            var snapshot = await store.TryReadAsync(context.RequestAborted);
            if (snapshot is null) return Results.Redirect("/manage");
            var page = snapshot.Website.Pages.FirstOrDefault(p => p.Id == id);
            return page is null ? Results.NotFound() : ManagementUi.Edit(context, snapshot, page);
        });
        app.MapPost("/manage/pages/{id}", async (string id, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            try
            {
                var fields = form.Where(f => f.Key.StartsWith("field.", StringComparison.Ordinal)).ToDictionary(f => f.Key[6..], f => f.Value.ToString(), StringComparer.Ordinal);
                await operations.EditPageAsync(form["revision"].ToString(), id, form["title"].ToString(), form["description"].ToString(), fields, context.RequestAborted);
                return Results.Redirect("/manage");
            }
            catch (Exception error) when (error is RevisionConflictException or ContentValidationException)
            {
                var snapshot = await store.ReadAsync(context.RequestAborted);
                var page = snapshot.Website.Pages.FirstOrDefault(p => p.Id == id);
                return page is null ? ManagementUi.Problem(error.Message, 404) : ManagementUi.Edit(context, snapshot, page, error.Message, error is RevisionConflictException ? 409 : 422, form);
            }
            catch (SiteNotInitializedException error) { return ManagementUi.Problem(error.Message, 409); }
        });
        app.MapPost("/manage/pages", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            try
            {
                await operations.AddPageAsync(form["revision"].ToString(), form["title"].ToString(), form["path"].ToString(), form["description"].ToString(), context.RequestAborted);
                return Results.Redirect("/manage");
            }
            catch (Exception error) when (error is RevisionConflictException or ContentValidationException or SiteNotInitializedException)
            { return ManagementUi.Problem(error.Message, error is ContentValidationException ? 422 : 409); }
        });
        app.MapPost("/manage/preview", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            try
            {
                var preview = await operations.PreviewAsync(form["revision"].ToString(), "/manage/preview/", context.RequestAborted);
                return Results.Redirect("/manage/preview/" + preview.Id + "/");
            }
            catch (Exception error) when (error is RevisionConflictException or ContentValidationException or SiteNotInitializedException)
            { return ManagementUi.Problem(error.Message, error is ContentValidationException ? 422 : 409); }
        });
        app.MapMethods("/manage/preview/{id}/{**path}", ["GET", "HEAD"], async (string id, string? path, HttpContext context) =>
        {
            var artifact = await store.ReadPreviewAsync(id, context.RequestAborted);
            if (artifact is null) { context.Response.StatusCode = 404; return; }
            await new PrerenderedDelivery(new FixedArtifactSource(artifact), new NoDeliveryCache()).DeliverAsync(context, path);
        });
        app.MapGet("/manage/export", async (HttpContext context) =>
        {
            var captured = await store.CaptureAsync(context.RequestAborted);
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry("content.json");
                await using (var target = entry.Open()) await JsonSerializer.SerializeAsync(target, captured.Snapshot, DemoContentSource.Json, context.RequestAborted);
                foreach (var (file, bytes) in captured.Assets)
                {
                    await using var target = zip.CreateEntry(file).Open();
                    await target.WriteAsync(bytes.ToArray(), context.RequestAborted);
                }
            }
            return Results.File(stream.ToArray(), "application/zip", "webspine-content.zip");
        });
        app.MapGet("/manage/settings", async (HttpContext context) =>
        {
            var snapshot = await store.TryReadAsync(context.RequestAborted);
            return snapshot is null ? Results.Redirect("/manage") : ManagementUi.Settings(context, snapshot);
        });
        app.MapPost("/manage/settings", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            try
            {
                await operations.SettingsAsync(form["revision"].ToString(), form["title"].ToString(), form["language"].ToString(), context.RequestAborted);
                return Results.Redirect("/manage");
            }
            catch (Exception error) when (error is RevisionConflictException or ContentValidationException)
            { return ManagementUi.Settings(context, await store.ReadAsync(context.RequestAborted), error.Message, error is RevisionConflictException ? 409 : 422, form); }
            catch (SiteNotInitializedException error) { return ManagementUi.Problem(error.Message, 409); }
        });
    }
}
