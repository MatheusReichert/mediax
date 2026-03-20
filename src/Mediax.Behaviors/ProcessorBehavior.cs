using Mediax.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Behaviors;

/// <summary>
/// Pipeline behavior that automatically runs all registered
/// <see cref="IRequestPreProcessor{TRequest}"/> and <see cref="IRequestPostProcessor{TRequest,TResponse}"/>
/// for every request. Register once as a global behavior.
/// </summary>
public sealed class ProcessorBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IRequestPreProcessor<TRequest>[] _preProcessors;
    private readonly IRequestPostProcessor<TRequest, TResponse>[] _postProcessors;

    public ProcessorBehavior(
        IEnumerable<IRequestPreProcessor<TRequest>> preProcessors,
        IEnumerable<IRequestPostProcessor<TRequest, TResponse>> postProcessors)
    {
        _preProcessors = preProcessors as IRequestPreProcessor<TRequest>[] ?? preProcessors.ToArray();
        _postProcessors = postProcessors as IRequestPostProcessor<TRequest, TResponse>[] ?? postProcessors.ToArray();
    }

    public async ValueTask<Result<TResponse>> Handle(
        TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        foreach (var pre in _preProcessors)
            await pre.Process(request, ct);

        var result = await next(request, ct);

        foreach (var post in _postProcessors)
            await post.Process(request, result, ct);

        return result;
    }
}
