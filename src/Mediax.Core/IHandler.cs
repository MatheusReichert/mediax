namespace Mediax.Core;

/// <summary>Handles a single request and returns a <see cref="Result{TResponse}"/>.</summary>
public interface IHandler<in TRequest, TResponse> 
    where TRequest : IRequest<TResponse>, allows ref struct
{
    ValueTask<Result<TResponse>> Handle(TRequest request, CancellationToken ct);
}

