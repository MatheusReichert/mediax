namespace Mediax.Core;

/// <summary>
/// Executes logic before a request is handled by its handler.
/// Register via DI as <c>IRequestPreProcessor&lt;TRequest&gt;</c>.
/// </summary>
public interface IRequestPreProcessor<in TRequest>
    where TRequest : allows ref struct
{
    ValueTask Process(TRequest request, CancellationToken ct);
}

/// <summary>
/// Executes logic after a request is handled by its handler.
/// Register via DI as <c>IRequestPostProcessor&lt;TRequest, TResponse&gt;</c>.
/// </summary>
public interface IRequestPostProcessor<in TRequest, TResponse>
    where TRequest : allows ref struct
{
    ValueTask Process(TRequest request, Result<TResponse> result, CancellationToken ct);
}
