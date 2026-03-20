using System.Diagnostics;
using Mediax.Core;

namespace Mediax.Behaviors;

/// <summary>
/// Creates an OpenTelemetry <see cref="Activity"/> span for each request, enabling distributed tracing.
/// </summary>
public sealed class TracingBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ActivitySource ActivitySource = new("Mediax");

    public async ValueTask<Result<TResponse>> Handle(
        TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        using var activity = ActivitySource.StartActivity(
            $"Mediax.Handle.{requestName}",
            ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag("mediax.request.type", typeof(TRequest).FullName);
            activity.SetTag("mediax.response.type", typeof(TResponse).FullName);
        }

        try
        {
            var result = await next(request, ct);

            if (activity != null)
            {
                activity.SetTag("mediax.success", result.IsSuccess);
                if (!result.IsSuccess)
                {
                    activity.SetTag("mediax.error.code", result.Error!.Code);
                    activity.SetTag("mediax.error.type", result.Error.Type.ToString());
                    activity.SetStatus(ActivityStatusCode.Error, result.Error.Message);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
