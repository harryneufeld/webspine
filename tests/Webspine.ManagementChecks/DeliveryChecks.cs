using System.Collections.Immutable;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Webspine.Caching.Memory;
using Webspine.Core;
using Webspine.Delivery;

static class DeliveryChecks
{
    public static async Task RunAsync()
    {
        var first = Artifact("first");
        var second = Artifact("second");
        var source = new SwitchingSource(first);
        using var cache = new MemoryDeliveryCache();
        var hook = new CountingHeaders();
        var delivery = new PrerenderedDelivery(source, cache, [hook], diagnostics: true);
        var miss = await Request(delivery);
        var hit = await Request(delivery);
        Require(miss.Response.Headers["X-Webspine-Cache"] == "miss" && hit.Response.Headers["X-Webspine-Cache"] == "hit", "Expected cache miss followed by hit.");
        Require(Bytes(miss).SequenceEqual(Bytes(hit)) && hook.Calls == 2 && hit.Response.Headers["X-Example"] == "2", "Hooks must run on every response without mutating reviewed bytes.");
        Require(Bytes(hit).SequenceEqual(first.Files[0].Bytes), "Delivery changed artifact bytes.");
        Console.WriteLine("PASS: Prerendered cache hits preserve exact bytes and execute delivery hooks.");

        source.Current = second;
        var changed = await Request(delivery);
        Require(changed.Response.Headers["X-Webspine-Cache"] == "miss" && Encoding.UTF8.GetString(Bytes(changed)) == "second", "Release change served old cached bytes.");
        source.Current = first;
        var restored = await Request(delivery);
        Require(restored.Response.Headers["X-Webspine-Cache"] == "hit" && Encoding.UTF8.GetString(Bytes(restored)) == "first", "Rollback namespace did not restore original output.");
        Console.WriteLine("PASS: Release changes and restored releases use independent cache namespaces.");

        foreach (var variant in new[] { "cookie", "authorization", "identity", "query" })
        {
            var bypass = await Request(delivery, configure: context =>
            {
                if (variant == "cookie") context.Request.Headers.Cookie = "session=example";
                if (variant == "authorization") context.Request.Headers.Authorization = "Bearer example";
                if (variant == "identity") context.User = new(new ClaimsIdentity([new Claim("sub", "example")], "test"));
                if (variant == "query") context.Request.QueryString = new("?filter=example");
            });
            Require(bypass.Response.Headers["X-Webspine-Cache"] == "bypass" && bypass.Response.Headers.CacheControl == "no-store", "Private/variant request entered shared cache.");
        }
        Console.WriteLine("PASS: Cookie, credential, authenticated and query requests bypass shared caching.");

        var head = await Request(delivery, configure: c => c.Request.Method = "HEAD");
        Require(Bytes(head).Length == 0 && head.Response.ContentLength == first.Files[0].Bytes.Length, "HEAD included a body or omitted length.");
        var conditional = await Request(delivery, configure: c => c.Request.Headers.IfNoneMatch = "W/" + hit.Response.Headers.ETag);
        Require(conditional.Response.StatusCode == 304 && Bytes(conditional).Length == 0, "Conditional GET did not return an empty 304.");
        var stale = await Request(delivery, configure: c => c.Request.Headers.IfNoneMatch = "\"old\"");
        Require(stale.Response.StatusCode == 200 && Bytes(stale).Length > 0, "Stale ETag suppressed new content.");
        Require((await Request(delivery, "../private")).Response.StatusCode == 404 && (await Request(delivery, "missing/")).Response.StatusCode == 404, "Invalid or unknown path was served.");
        var post = await Request(delivery, configure: c => c.Request.Method = "POST");
        Require(post.Response.StatusCode == 405 && Bytes(post).Length == 0, "Prerendered delivery accepted a write method.");
        Console.WriteLine("PASS: HEAD, ETag revalidation, stale validators and unsafe/missing routes.");

        var uncached = new PrerenderedDelivery(source, new NoDeliveryCache(), diagnostics: true);
        Require((await Request(uncached)).Response.Headers["X-Webspine-Cache"] == "miss" && (await Request(uncached)).Response.Headers["X-Webspine-Cache"] == "miss", "Cache replacement was not honored.");
        try
        {
            await Request(new PrerenderedDelivery(source, cache, [new UnsafeHeaders()]));
            throw new Exception("Unsafe header hook was accepted.");
        }
        catch (InvalidOperationException) { }
        Console.WriteLine("PASS: Cache can be substituted and delivery hooks cannot override security headers.");
    }

    private static BuiltArtifact Artifact(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text).ToImmutableArray();
        var digest = BuildPipeline.Hash(bytes.AsSpan());
        return new(new("demo", "fixture"), text, 1, "design", [new("index.html", bytes, digest)], digest);
    }
    private static async Task<DefaultHttpContext> Request(PrerenderedDelivery delivery, string? path = null, Action<DefaultHttpContext>? configure = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        configure?.Invoke(context);
        await delivery.DeliverAsync(context, path);
        return context;
    }
    private static byte[] Bytes(DefaultHttpContext context) => ((MemoryStream)context.Response.Body).ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private sealed class SwitchingSource(BuiltArtifact current) : IPublishedArtifactSource
    {
        public BuiltArtifact Current { get; set; } = current;
        public ValueTask<BuiltArtifact> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Current);
    }
    private sealed class CountingHeaders : IDeliveryHeaders
    {
        public string Id => "counting";
        public int Calls { get; private set; }
        public ValueTask<ImmutableDictionary<string, string>> GetAsync(DeliveryRepresentation representation, CancellationToken cancellationToken)
            => ValueTask.FromResult(ImmutableDictionary<string, string>.Empty.Add("X-Example", (++Calls).ToString()));
    }
    private sealed class UnsafeHeaders : IDeliveryHeaders
    {
        public string Id => "unsafe";
        public ValueTask<ImmutableDictionary<string, string>> GetAsync(DeliveryRepresentation representation, CancellationToken cancellationToken)
            => ValueTask.FromResult(ImmutableDictionary<string, string>.Empty.Add("X-Content-Type-Options", "unsafe"));
    }
}
