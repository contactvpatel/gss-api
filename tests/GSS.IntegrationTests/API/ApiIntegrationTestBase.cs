using GSS.IntegrationTests.Fixtures;

namespace GSS.IntegrationTests.API;

/// <summary>
/// Base for API integration tests: shared factory, client, and database reset. Add feature-specific creation helpers alongside each feature.
/// </summary>
public abstract class ApiIntegrationTestBase
{
    private const int HttpTimeoutSeconds = 30;

    protected readonly WebAppFactory Factory;
    protected readonly HttpClient Client;

    protected ApiIntegrationTestBase(WebAppFactory factory)
    {
        WebAppFactory.EnsureTestDatabaseExists();
        Factory = factory;
        Client = factory.CreateClient();
        Client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);
    }

    protected async Task ResetDatabaseAsync()
    {
        await Factory.ResetDatabaseAsync();
    }
}
