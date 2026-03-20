namespace Mediax.Core;

/// <summary>Wraps a request with a cancellation timeout applied before dispatch.</summary>
public sealed class TimeoutDecorator<TResponse> : IRequest<TResponse>
{
    public IRequest<TResponse> Inner { get; }
    public TimeSpan Timeout { get; }

    public TimeoutDecorator(IRequest<TResponse> inner, TimeSpan timeout)
    {
        Inner = inner;
        Timeout = timeout;
    }
}

/// <summary>Wraps a request with automatic retry logic applied before dispatch.</summary>
public sealed class RetryDecorator<TResponse> : IRequest<TResponse>
{
    public IRequest<TResponse> Inner { get; }
    public int MaxAttempts { get; }

    public RetryDecorator(IRequest<TResponse> inner, int maxAttempts)
    {
        Inner = inner;
        MaxAttempts = maxAttempts;
    }
}
