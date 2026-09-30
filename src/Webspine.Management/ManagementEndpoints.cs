using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
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
        var directory = app.Configuration["Management:DataDirectory"] ?? Path.Combine(app.Environment.ContentRootPath, ".local");
        var store = new SqliteContentSource(Path.Combine(directory, "webspine.db"));
        await store.InitializeSchemaAsync();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/manage"))
            {
                var host = context.Request.Host.Host;
                if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
                    !(host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1" or "[::1]"))
                { context.Response.StatusCode = 403; return; }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 262144;
                if (HttpMethods.IsPost(context.Request.Method))
                {
                    try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                    catch (AntiforgeryValidationException) { await ManagementUi.Problem("This form expired or could not be verified. Reopen the page and try again.", 400).ExecuteAsync(context); return; }
                }
            }
            await next(context);
        });
        app.MapGet("/manage/assets/editor.css", () => Results.Text(ManagementUi.Css, "text/css; charset=utf-8"));
        app.MapGet("/manage", async (HttpContext context) =>
        {
            var snapshot = await store.TryReadAsync(context.RequestAborted);
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
                await store.EditPageAsync(form["revision"].ToString(), id, form["title"].ToString(), form["description"].ToString(), fields, context.RequestAborted);
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
                await store.AddPageAsync(form["revision"].ToString(), form["title"].ToString(), form["path"].ToString(), form["description"].ToString(), context.RequestAborted);
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
                var captured = await store.CaptureAsync(context.RequestAborted);
                if (captured.Snapshot.Revision != form["revision"].ToString()) throw new RevisionConflictException();
                var id = Guid.NewGuid().ToString("N");
                var artifact = await DemoSite.BuildSnapshotAsync(captured.Snapshot, captured.Assets, "/manage/preview/" + id, context.RequestAborted);
                await store.SavePreviewAsync(id, artifact, context.RequestAborted);
                return Results.Redirect("/manage/preview/" + id + "/");
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
    }
}
