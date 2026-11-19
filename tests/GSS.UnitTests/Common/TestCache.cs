using Caching.NET;
using Caching.NET.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSS.UnitTests.Common;

/// <summary>
/// Real in-memory <see cref="ICacheService"/> for unit tests (mock it only to inject failures).
/// </summary>
public static class TestCache
{
    /// <summary>New isolated cache.</summary>
    public static ICacheService Create()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddCaching(builder => builder.UseInMemory().WithApplicationPrefix("gss-unit-tests"));
        return services.BuildServiceProvider().GetRequiredService<ICacheService>();
    }

    /// <summary>Caching turned off (<c>CacheOptions:Enabled=false</c>, the appsettings default): every call runs the factory.</summary>
    public static ICacheService CreateDisabled()
    {
        ServiceCollection services = new();
        services.AddLogging();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CacheOptions:Enabled"] = "false" })
            .Build();
        services.AddCaching(configuration);
        return services.BuildServiceProvider().GetRequiredService<ICacheService>();
    }
}
