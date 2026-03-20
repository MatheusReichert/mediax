using Mediax.Core;
using Mediax.Behaviors;

namespace Mediax.Tests.Fixtures;

// ----- Requests -----

public sealed record EchoQuery(string Message) : IQuery<string>;

public sealed record FailingCommand(string Reason) : ICommand<Unit>;

public sealed record AddNumbersCommand(int A, int B) : ICommand<int>;

public sealed record SampleEvent(string Payload) : IEvent;

// ----- Handlers -----

[Handler(Lifetime = HandlerLifetime.Scoped)]
[UseBehavior(typeof(LogBehavior<,>))]
public sealed class EchoQueryHandler : IHandler<EchoQuery, string>
{
    public ValueTask<Result<string>> Handle(EchoQuery request, CancellationToken ct)
        => ValueTask.FromResult(Result<string>.Ok(request.Message));
}

[Handler(Lifetime = HandlerLifetime.Scoped)]
public sealed class FailingCommandHandler : IHandler<FailingCommand, Unit>
{
    public ValueTask<Result<Unit>> Handle(FailingCommand request, CancellationToken ct)
        => ValueTask.FromResult(Result<Unit>.Fail(Error.Internal("TEST_FAIL", request.Reason)));
}

[Handler(Lifetime = HandlerLifetime.Scoped)]
[UseBehavior(typeof(ValidationBehavior<,>))]
public sealed class AddNumbersHandler : IHandler<AddNumbersCommand, int>
{
    public ValueTask<Result<int>> Handle(AddNumbersCommand request, CancellationToken ct)
        => ValueTask.FromResult(Result<int>.Ok(request.A + request.B));
}

[Handler(Lifetime = HandlerLifetime.Scoped)]
public sealed class SampleEventHandler : IHandler<SampleEvent, Unit>
{
    public static readonly List<string> ReceivedPayloads = new();

    public ValueTask<Result<Unit>> Handle(SampleEvent request, CancellationToken ct)
    {
        ReceivedPayloads.Add(request.Payload);
        return ValueTask.FromResult(Result<Unit>.Ok(Unit.Value));
    }
}

public sealed record SampleStreamRequest(int Count) : IStreamRequest<int>;

[Handler(Lifetime = HandlerLifetime.Scoped)]
public sealed class SampleStreamHandler : IStreamHandler<SampleStreamRequest, int>
{
    public async global::System.Collections.Generic.IAsyncEnumerable<int> Handle(SampleStreamRequest request, [global::System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (int i = 0; i < request.Count; i++)
        {
            yield return i;
            await global::System.Threading.Tasks.Task.Yield();
        }
    }
}
