using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Webspine.Core;

namespace Webspine.Delivery;

public interface IPublishedArtifactSource
{
    ValueTask<BuiltArtifact> ReadAsync(CancellationToken cancellationToken);
}

public sealed class FixedArtifactSource(BuiltArtifact artifact) : IPublishedArtifactSource
{
    public ValueTask<BuiltArtifact> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(artifact);
    }
}

public sealed record DeliveryRepresentation(ArtifactFile File, string ContentType);
public readonly record struct DeliveryCacheKey(string ReleaseDigest, string Path, string FileDigest);

public interface IDeliveryCache
{
    ValueTask<DeliveryRepresentation?> GetAsync(DeliveryCacheKey key, CancellationToken cancellationToken);
    ValueTask SetAsync(DeliveryCacheKey key, DeliveryRepresentation value, TimeSpan lifetime, CancellationToken cancellationToken);
}

// Runs on cache hits and misses; body mutation is deliberately outside this hook.
public interface IDeliveryHeaders
{
    string Id { get; }
    ValueTask<ImmutableDictionary<string, string>> GetAsync(DeliveryRepresentation representation, CancellationToken cancellationToken);
}

public sealed class PrerenderedDelivery
{
    private readonly IPublishedArtifactSource source;
    private readonly IDeliveryCache cache;
    private readonly ImmutableArray<IDeliveryHeaders> hooks;
    private readonly bool diagnostics;
    private readonly DesignScriptPolicy? scripts;

    public PrerenderedDelivery(IPublishedArtifactSource source, IDeliveryCache cache,
        IEnumerable<IDeliveryHeaders>? hooks = null, bool diagnostics = false, DesignScriptPolicy? scripts = null)
    {
        this.source = source;
        this.cache = cache;
        this.hooks = hooks?.ToImmutableArray() ?? [];
        this.diagnostics = diagnostics;
        this.scripts = scripts;
        if (this.hooks.Any(h => string.IsNullOrWhiteSpace(h.Id)) || this.hooks.Select(h => h.Id).Distinct().Count() != this.hooks.Length)
            throw new ArgumentException("Delivery hook IDs must be nonempty and unique.");
    }

    public async Task DeliverAsync(HttpContext context, string? path)
    {
        var ct = context.RequestAborted;
        ct.ThrowIfCancellationRequested();
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = 405;
            context.Response.Headers.Allow = "GET, HEAD";
            return;
        }
        var requested = path ?? "";
        var filePath = requested.Length == 0 ? "index.html" : requested.EndsWith('/') ? requested + "index.html" : requested;
        try { ArtifactPaths.Validate(filePath); }
        catch (ContentValidationException) { context.Response.StatusCode = 404; return; }
        // Resolve once per request: concurrent promotion cannot mix files/revision within a response.
        var release = await source.ReadAsync(ct);
        if (filePath.EndsWith(".js", StringComparison.Ordinal) && scripts?.Allows(release, filePath) != true)
        { context.Response.StatusCode = 404; return; }
        var file = release.Files.FirstOrDefault(f => f.Path == filePath);
        if (file is null) { context.Response.StatusCode = 404; return; }
        var key = new DeliveryCacheKey(release.Digest, file.Path, file.Digest);
        var eligible = context.User.Identity?.IsAuthenticated != true &&
            !context.Request.Headers.ContainsKey("Authorization") && !context.Request.Headers.ContainsKey("Cookie") &&
            !context.Request.QueryString.HasValue;
        var representation = eligible ? await cache.GetAsync(key, ct) : null;
        var hit = representation is not null;
        representation ??= new(file, ContentType(filePath));
        if (eligible && !hit) await cache.SetAsync(key, representation, TimeSpan.FromMinutes(5), ct);

        foreach (var hook in hooks)
        {
            var headers = await hook.GetAsync(representation, ct);
            foreach (var (name, value) in headers)
            {
                // Extension headers cannot replace protocol, cache, cookie or security headers.
                if (!name.StartsWith("X-", StringComparison.OrdinalIgnoreCase) || name.Equals("X-Content-Type-Options", StringComparison.OrdinalIgnoreCase) ||
                    name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || value.Any(c => char.IsControl(c)))
                    throw new InvalidOperationException("Delivery hooks may add only safe custom X- headers.");
                context.Response.Headers[name] = value;
            }
        }
        context.Response.ContentType = representation.ContentType;
        context.Response.Headers.ETag = $"\"{file.Digest}\"";
        // This first delivery host is a development demo, never a shared customer preview cache.
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
        if (scripts?.Enabled(release) == true) context.Response.Headers.ContentSecurityPolicy += "; script-src 'self'";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (diagnostics) context.Response.Headers["X-Webspine-Cache"] = eligible ? hit ? "hit" : "miss" : "bypass";
        var matches = context.Request.Headers.IfNoneMatch.ToString().Split(',').Select(v => v.Trim())
            .Any(v => v == "*" || v == context.Response.Headers.ETag.ToString() || v == "W/" + context.Response.Headers.ETag);
        if (matches) { context.Response.StatusCode = 304; return; }
        context.Response.ContentLength = file.Bytes.Length;
        if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.Body.WriteAsync(file.Bytes.ToArray(), ct);
    }

    private static string ContentType(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html; charset=utf-8", ".css" => "text/css; charset=utf-8", ".js" => "text/javascript; charset=utf-8",
        ".svg" => "image/svg+xml", ".json" => "application/json", ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "application/octet-stream"
    };
}
