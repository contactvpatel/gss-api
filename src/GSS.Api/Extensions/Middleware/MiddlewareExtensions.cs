using GSS.Api.Extensions.Core;
using GSS.Api.Extensions.Features;
using GSS.Api.Middleware;

namespace GSS.Api.Extensions.Middleware;

/// <summary>
/// Middleware pipeline setup.
/// </summary>
public static class MiddlewareExtensions
{
    /// <summary>
    /// Builds the pipeline with ApiPipeline.NET, which checks the phase order at startup. Settings: <c>ApiPipeline</c> section.
    /// </summary>
    public static void ConfigureMiddleware(this WebApplication app)
    {
        bool isTesting = app.Configuration.IsTestingEnvironment();

        // Forwarded headers first (client scheme and IP); exception handling inside the correlation scope so error
        // logs carry CorrelationId; status-code pages right after it, so an empty-bodied 4xx/5xx (unmatched route 404, 405,
        // 415) gets the envelope via IProblemDetailsService; authentication before rate limiting so limits partition per user.
        app.UseApiPipeline(pipeline =>
        {
            pipeline
                .WithForwardedHeaders()
                .WithCorrelationId()
                .WithSecurityHeaders()
                .WithMiddleware(a => a.UseExceptionHandler())
                .WithMiddleware(a => a.UseStatusCodePages())
                .WithMiddleware(a => a.UseHttpsOnly(isTesting))
                .WithMiddleware(a => a.UseSwaggerUiIfEnabled(app.Configuration))
                .WithCors()
                .WithAuthentication()
                .WithRateLimiting()
                .WithResponseCompression()
                .WithAuthorization();

            // Outside Development a disabled security phase fails startup; integration tests turn rate limiting off.
            if (!isTesting)
            {
                pipeline.RequireSecurityFeaturesEnabled();
            }
        });

        app.MapControllers();
    }

    /// <summary>
    /// Answers plain HTTP with a 400 error envelope, except /internal/health probes. Skipped in Testing. Runs after forwarded
    /// headers so <c>Request.IsHttps</c> is the client's scheme. HSTS comes from the security headers phase.
    /// </summary>
    private static void UseHttpsOnly(this IApplicationBuilder app, bool isTesting)
    {
        if (isTesting)
        {
            return;
        }

        // Probes hit the container port over HTTP, so /internal/health is exempt; the aliases are not.
        app.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps
                && !context.Request.Path.StartsWithSegments(HealthCheckExtensions.InternalProbePathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Same envelope as every other error (ApiErrorProblemDetailsService).
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = { Status = StatusCodes.Status400BadRequest, Detail = "HTTPS is required." }
                });
                return;
            }
            await next();
        });
    }
}
