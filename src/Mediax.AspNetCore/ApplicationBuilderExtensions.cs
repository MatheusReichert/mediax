using Mediax.Runtime;
using Microsoft.AspNetCore.Builder;

namespace Mediax.AspNetCore;

/// <summary>Extension methods for <see cref="IApplicationBuilder"/> to initialize the Mediax runtime.</summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Initializes the Mediax runtime with the application's service provider.
    /// Must be called before dispatching any requests (typically early in the middleware pipeline).
    /// </summary>
    public static IApplicationBuilder UseMediax(this IApplicationBuilder app)
    {
        MediaxRuntime.Init(app.ApplicationServices);
        return app;
    }
}
