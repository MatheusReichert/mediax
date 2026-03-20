using Mediax.Core;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;

namespace Mediax.Behaviors;

/// <summary>
/// Applies a circuit breaker around handler execution using Microsoft.Extensions.Resilience (Polly v8).
/// Register a named <see cref="ResiliencePipeline"/> with key "mediax-circuit-breaker" to customize the policy.
/// </summary>
public sealed class CircuitBreakerBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ResiliencePipeline _pipeline;

    public CircuitBreakerBehavior(ResiliencePipelineProvider<string> pipelineProvider)
    {
        // Use a named pipeline if registered, otherwise use an empty (no-op) pipeline.
        // The key "mediax-circuit-breaker" should be configured via AddResiliencePipeline(...)
        // in the application's DI setup.
        try
        {
            _pipeline = pipelineProvider.GetPipeline("mediax-circuit-breaker");
        }
        catch
        {
            _pipeline = ResiliencePipeline.Empty;
        }
    }

    public async ValueTask<Result<TResponse>> Handle(
        TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        return await _pipeline.ExecuteAsync(
            async (token) => await next(request, token),
            cancellationToken: ct);
    }
}
