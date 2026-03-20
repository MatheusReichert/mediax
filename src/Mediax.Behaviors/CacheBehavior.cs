using System.Text.Json;
using Mediax.Core;
using Microsoft.Extensions.Caching.Distributed;

namespace Mediax.Behaviors;

/// <summary>
/// Caches successful responses in <see cref="IDistributedCache"/> when the request type is decorated
/// with <see cref="CacheAttribute"/>. Respects the configured TTL.
/// </summary>
public sealed class CacheBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IDistributedCache _cache;
    private readonly CacheAttribute? _attr;

    public CacheBehavior(IDistributedCache cache)
    {
        _cache = cache;
        _attr = typeof(TRequest).GetCustomAttributes(typeof(CacheAttribute), inherit: true)
            .OfType<CacheAttribute>()
            .FirstOrDefault();
    }

    public async ValueTask<Result<TResponse>> Handle(
        TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        if (_attr == null)
            return await next(request, ct);

        var key = $"mediax:{typeof(TRequest).FullName}:{JsonSerializer.Serialize(request)}";

        var cached = await _cache.GetStringAsync(key, ct);
        if (cached != null)
        {
            var value = JsonSerializer.Deserialize<TResponse>(cached);
            return Result<TResponse>.Ok(value!);
        }

        var result = await next(request, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            await _cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(result.Value),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_attr.Ttl)
                },
                ct);
        }

        return result;
    }
}
