using Mediax.Core;
using Microsoft.Extensions.Logging;

namespace Mediax.Behaviors;

/// <summary>
/// Logs the start, completion, and failure of every request with structured metadata.
/// </summary>
public sealed class LogBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LogBehavior<TRequest, TResponse>> _logger;

    public LogBehavior(ILogger<LogBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async ValueTask<Result<TResponse>> Handle(
        TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogInformation("Mediax | Handling {RequestName} {@Request}", requestName, request);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Result<TResponse> result;
        try
        {
            result = await next(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mediax | Unhandled exception in {RequestName}", requestName);
            throw;
        }
        sw.Stop();

        if (result.IsSuccess)
            _logger.LogInformation(
                "Mediax | Handled {RequestName} in {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);
        else
            _logger.LogWarning(
                "Mediax | {RequestName} failed in {ElapsedMs}ms — [{ErrorCode}] {ErrorMessage}",
                requestName, sw.ElapsedMilliseconds, result.Error!.Code, result.Error.Message);

        return result;
    }
}
