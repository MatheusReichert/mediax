// Mediax stream handler for benchmark.
using Mediax.Core;

namespace Mediax.Benchmarks.Handlers;

public sealed record MediaxCountStreamRequest(int Count) : IStreamRequest<int>;

[Handler]
public sealed class MediaxCountStreamHandler : IStreamHandler<MediaxCountStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(
        MediaxCountStreamRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (int i = 0; i < request.Count; i++)
        {
            yield return i;
            await Task.Yield(); // simulate async item production
        }
    }
}
