using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Webspine.RazorPrototype;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var artifact = await new PrototypeRenderer().BuildAsync(await PrototypeInputs.CaptureAsync());
var files = artifact.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
var app = builder.Build();
// Dedicated fixture host, fixed loopback listener; no customer/management state.
app.Urls.Clear();
app.Urls.Add("http://127.0.0.1:9094");
app.MapMethods("/{**path}", ["GET", "HEAD"], async (string? path, HttpContext context) =>
{
    if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
    { context.Response.StatusCode = 403; return; }
    var filePath = path is null or "" ? "index.html" : path.EndsWith('/') ? path + "index.html" : path;
    if (!files.TryGetValue(filePath, out var file)) { context.Response.StatusCode = 404; return; }
    context.Response.ContentType = file.Path.EndsWith(".html", StringComparison.Ordinal) ? "text/html; charset=utf-8" :
        file.Path.EndsWith(".css", StringComparison.Ordinal) ? "text/css; charset=utf-8" :
        file.Path.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript; charset=utf-8" :
        file.Path.EndsWith(".svg", StringComparison.Ordinal) ? "image/svg+xml" : "application/json; charset=utf-8";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; script-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
    context.Response.ContentLength = file.Bytes.Length;
    if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.Body.WriteAsync(file.Bytes.ToArray(), context.RequestAborted);
});
Console.WriteLine($"Razor design prototype: http://127.0.0.1:9094/ — retained artifact {artifact.Digest}");
await app.RunAsync();
