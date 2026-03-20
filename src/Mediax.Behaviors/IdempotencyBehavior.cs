using Mediax.Core;
using Microsoft.Extensions.Caching.Distributed;

namespace Mediax.Behaviors;

/// <summary>
/// Deduplicates requests by storing completed responses in <see cref="IDistributedCache"/>.
/// The request must implement <see cref="IIdempotent"/> to expose an idempotency key.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IDistributedCache _cache;

    public IdempotencyBehavior(IDistributedCache cache) => _cache = cache;

    public async ValueTask<Result<TResponse>> Handle(
        TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        if (request is not IIdempotent idempotent)
            return await next(request, ct);

        var key = $"mediax:idempotency:{idempotent.IdempotencyKey}";

        var existing = await _cache.GetStringAsync(key, ct);
        if (existing != null)
        {
            var cached = System.Text.Json.JsonSerializer.Deserialize<TResponse>(existing);
            return Result<TResponse>.Ok(cached!);
        }

        var result = await next(request, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            await _cache.SetStringAsync(
                key,
                System.Text.Json.JsonSerializer.Serialize(result.Value),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                },
                ct);
        }

        return result;
    }
}

/// <summary>Implement this interface on commands that require idempotency guarantees.</summary>
public interface IIdempotent
{
    string IdempotencyKey { get; }
}
