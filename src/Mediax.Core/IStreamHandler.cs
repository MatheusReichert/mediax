namespace Mediax.Core;

/// <summary>Optional handler that yields multiple responses for a streaming request.</summary>
public interface IStreamHandler<in TRequest, out TResponse> where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken ct);
}
