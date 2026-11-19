using System.Net;
using System.Text.Json;
using FluentAssertions;
using GSS.IntegrationTests.Fixtures;
using Xunit;

namespace GSS.IntegrationTests.API;

[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class HealthApiIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    [Fact]
    public async Task GetLiveness_WhenCalled_Returns200WithoutDependencyChecks()
    {
        var response = await Client.GetAsync("/internal/health/liveness");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkNames = await GetCheckNamesAsync(response);
        checkNames.Should().NotContain(["database-read", "database-write"],
            because: "liveness must stay process-only so a database outage never restarts pods");
    }

    [Fact]
    public async Task GetStartup_AfterApplicationStarted_Returns200()
    {
        var response = await Client.GetAsync("/internal/health/startup");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetReadiness_WhenDatabaseReachable_Returns200WithDatabaseChecks()
    {
        await ResetDatabaseAsync();

        var response = await Client.GetAsync("/internal/health/readiness");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkNames = await GetCheckNamesAsync(response);
        checkNames.Should().Contain(["database-read", "database-write"]);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/healthz/ready")]
    [InlineData("/internal/health/ready")]
    public async Task GetAliasRoute_WhenCalled_Returns200(string path)
    {
        var response = await Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The cardinal rule: a database outage pulls the pod from rotation (readiness 503) but never restarts it
    /// (liveness 200), and the failure is reported without connection details.
    /// </summary>
    [Fact]
    public async Task ReadDatabaseDown_ReadinessIs503Sanitized_LivenessStays200()
    {
        // Arrange - reads fail only after startup, so startup validation and migrations already passed.
        _ = Factory.Server;
        Factory.Connections.FailReads = true;

        try
        {
            // Act - probe results are cached briefly, so wait for the outage to show.
            (var readiness, string body) = await WaitForReadinessAsync(HttpStatusCode.ServiceUnavailable);
            var liveness = await Client.GetAsync("/internal/health/liveness");

            // Assert
            readiness.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            using JsonDocument json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("entries").GetProperty("database-read").GetProperty("status").GetString()
                .Should().Be("Unhealthy");
            body.Should().NotContain(SwitchableConnectionFactory.SecretPassword, because: "failures are sanitized")
                .And.NotContain("Port=1");

            liveness.StatusCode.Should().Be(HttpStatusCode.OK, because: "liveness must not depend on the database");
        }
        finally
        {
            // Leave no cached 503 behind for the next test.
            Factory.Connections.FailReads = false;
            (var recovered, _) = await WaitForReadinessAsync(HttpStatusCode.OK);
            recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>Polls readiness until it returns <paramref name="expected"/> or about 10 s pass (results are cached 5 s).</summary>
    private async Task<(HttpResponseMessage Response, string Body)> WaitForReadinessAsync(HttpStatusCode expected)
    {
        var response = await Client.GetAsync("/internal/health/readiness");
        for (int attempt = 0; attempt < 40 && response.StatusCode != expected; attempt++)
        {
            await Task.Delay(250);
            response = await Client.GetAsync("/internal/health/readiness");
        }

        return (response, await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<string>> GetCheckNamesAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        using JsonDocument json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("status", out _).Should().BeTrue();

        return json.RootElement.GetProperty("entries").EnumerateObject().Select(entry => entry.Name).ToList();
    }
}
