using Microsoft.Extensions.Caching.Memory;
using Webspine.Delivery;

namespace Webspine.Caching.Memory;

public sealed class MemoryDeliveryCache : IDeliveryCache, IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });
    public ValueTask<DeliveryRepresentation?> GetAsync(DeliveryCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        cache.TryGetValue(key, out DeliveryRepresentation? value);
        return ValueTask.FromResult(value);
    }
    public ValueTask SetAsync(DeliveryCacheKey key, DeliveryRepresentation value, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        cache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime, Size = value.File.Bytes.Length + 1024L });
        return ValueTask.CompletedTask;
    }
    public void Dispose() => cache.Dispose();
}

public sealed class NoDeliveryCache : IDeliveryCache
{
    public ValueTask<DeliveryRepresentation?> GetAsync(DeliveryCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<DeliveryRepresentation?>(null);
    }
    public ValueTask SetAsync(DeliveryCacheKey key, DeliveryRepresentation value, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
