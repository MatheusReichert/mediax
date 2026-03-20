using Mediax.Core;
using Polly;
using Polly.Registry;

namespace Mediax.Behaviors;

/// <summary>
/// A behavior that wraps execution in a Polly resilience pipeline.
/// The pipeline is resolved from <see cref="ResiliencePipelineRegistry{TKey}"/> using the request type name by default.
/// </summary>
public sealed class PollyBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ResiliencePipeline<Result<TResponse>> _pipeline;

    public PollyBehavior(ResiliencePipelineRegistry<string> registry)
    {
        // By default, it looks for a pipeline named after the request type.
        // If not found, it falls back to a "default" pipeline.
        _pipeline = registry.GetPipeline<Result<TResponse>>(typeof(TRequest).Name) 
                  ?? registry.GetPipeline<Result<TResponse>>("default");
    }

    public ValueTask<Result<TResponse>> Handle(TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        return _pipeline.ExecuteAsync(token => next(request, token), ct);
    }
}
