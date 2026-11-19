using Caching.NET.Extensions;
using dotenv.net;
using GSS.Api.Extensions.Core;
using GSS.Api.Extensions.Features;
using GSS.Api.Extensions.Middleware;
using OpenTelemetry.NET.Extensions;
using Serilog;

namespace GSS.Api;

/// <summary>
/// Entry point. An explicit class so WebApplicationFactory can reference it.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // Load .env.local in debug mode only (debugger attached in IDE; never in production).
        if (System.Diagnostics.Debugger.IsAttached)
        {
            string? envPath = FindEnvFile(".env.local");
            if (envPath != null)
            {
                DotEnv.Load(options: new DotEnvOptions(envFilePaths: [envPath]));
            }
        }

        // Bootstrap logger so startup failures are logged before the real logger exists.
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting GSS API");

            // appsettings.json plus env var / command-line overrides; no per-environment files.
            var builder = WebApplication.CreateBuilder(args);

            builder.AddObservability();

            builder.Services.ConfigureApiServices(builder.Configuration);

            var app = builder.Build();

            // ApiPipeline.NET's guards read IHostEnvironment; align it with AppSettings:Environment first.
            app.UseConfiguredEnvironment();

            app.ConfigureMiddleware();

            app.ConfigureHealthCheckEndpoints();

            app.ConfigureOpenApiEndpoints();

            // Fail fast on a misconfigured cache.
            app.Services.ValidateCachingRegistration();

            await app.StartAsync();

            Log.Information("GSS API started successfully");

            await app.WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "GSS API terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Search for .env.local starting from current directory, walking up to repo root.
    /// </summary>
    private static string? FindEnvFile(string fileName)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            string envPath = Path.Combine(current.FullName, fileName);
            if (File.Exists(envPath))
            {
                return envPath;
            }

            current = current.Parent;
        }

        return null;
    }
}
