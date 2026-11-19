using System.Net.Http.Headers;
using DbUp;
using DbUp.Engine.Output;
using GSS.Api;
using GSS.Business.Services.Interfaces;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Util.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>
/// Runs the API against the test database from <see cref="TestDatabaseSettings"/> (never the developer database).
/// Creates the database if missing; the app's startup migrations create the schema.
/// </summary>
public class WebAppFactory : WebApplicationFactory<Program>
{
    private static bool s_databaseEnsured;

    /// <summary>Creates the test database if missing. Call before CreateClient.</summary>
    public static void EnsureTestDatabaseExists()
    {
        if (s_databaseEnsured)
        {
            return;
        }

        EnsureDatabase.For.PostgresqlDatabase(TestDatabaseSettings.ConnectionString(), new NullUpgradeLog());
        s_databaseEnsured = true;
    }

    private sealed class NullUpgradeLog : IUpgradeLog
    {
        public void LogTrace(string format, params object[] args) { }
        public void LogDebug(string format, params object[] args) { }
        public void LogInformation(string format, params object[] args) { }
        public void LogWarning(string format, params object[] args) { }
        public void LogError(string format, params object[] args) { }
        public void LogError(Exception ex, string format, params object[] args) { }
    }

    /// <summary>Truncates all gss tables; call at the start of each test. Retries on deadlock (40P01).</summary>
    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        // Start the host first so its migrations create the schema.
        _ = Server;
        await TestDatabaseReset.ResetAsync(TestDatabaseSettings.ConnectionString(), cancellationToken).ConfigureAwait(false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Dictionary<string, string?> overrides = new Dictionary<string, string?>
        {
            ["AppSettings:Environment"] = "Testing",
            ["CacheOptions:Enabled"] = "false",
            ["OpenTelemetryOptions:EnableTracing"] = "false",
            ["OpenTelemetryOptions:EnableMetrics"] = "false",
            ["OpenTelemetryOptions:EnableLogging"] = "false",
            ["ApiPipeline:RateLimiting:Enabled"] = "false",
            // Testing is not Development, so ApiPipeline.NET refuses a loopback-only proxy trust set.
            ["ApiPipeline:ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8",
            ["ApiPipeline:ForwardedHeaders:ClearDefaultProxies"] = "true",
        };

        // Override Read and Write: appsettings.json points at the dev database.
        foreach (var setting in TestDatabaseSettings.ToConfigurationOverrides())
        {
            overrides[setting.Key] = setting.Value;
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(overrides));

        // Fake SSO only; the real auth handler still runs.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISsoService>();
            services.AddSingleton<ISsoService, FakeSsoService>();

            // Test-only endpoints (ProtectedProbeController, PagedProbeController, IdempotencyProbeController) for authentication, OpenAPI and idempotency tests.
            services.AddControllers().AddApplicationPart(typeof(ProtectedProbeController).Assembly);

            // Pass-through wrapper so a test can simulate a database outage after startup.
            services.RemoveAll<IDapperConnectionFactory>();
            services.AddSingleton(serviceProvider =>
                new SwitchableConnectionFactory(ActivatorUtilities.CreateInstance<DapperConnectionFactory>(serviceProvider)));
            services.AddSingleton<IDapperConnectionFactory>(serviceProvider =>
                serviceProvider.GetRequiredService<SwitchableConnectionFactory>());
        });
    }

    /// <summary>The app's connection factory; set <see cref="SwitchableConnectionFactory.FailReads"/> to simulate a read outage.</summary>
    public SwitchableConnectionFactory Connections => Services.GetRequiredService<SwitchableConnectionFactory>();

    /// <summary>Default client sends a valid test token; tests clear it for anonymous calls.</summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FakeSsoService.ValidToken);
    }
}
