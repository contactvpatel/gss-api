using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GSS.Api.Models;
using GSS.Core.Helpers;
using GSS.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GSS.IntegrationTests.API;

/// <summary>
/// (Runs against the test-only <see cref="IdempotencyProbeController"/>.) A POST with an <c>Idempotency-Key</c> runs once: a retry gets the first response, a different request with the same key
/// is refused, an error leaves no trace, and the stored response commits with the create or not at all.
/// </summary>
[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class IdempotencyKeyIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    [Fact]
    public async Task Retry_WithTheSameKeyAndBody_ReplaysTheFirstResponseAndCreatesOneRow()
    {
        await PrepareAsync();
        string name = NewName();

        using var first = await PostProbeAsync("key-1", name);
        using var retry = await PostProbeAsync("key-1", name);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created, "the retry sees what the first request returned, not a 409 for the name");
        retry.Headers.GetValues(IdempotencyLimits.ReplayedHeaderName).Should().Equal("true");
        first.Headers.Contains(IdempotencyLimits.ReplayedHeaderName).Should().BeFalse();
        retry.Headers.Location.Should().Be(first.Headers.Location, "the replay keeps the Location header");
        (await retry.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        (await CountAsync("SELECT count(*) FROM gss.idempotency_probe WHERE name = @v", name)).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM gss.idempotencykey")).Should().Be(1);
    }

    [Fact]
    public async Task SameKey_WithADifferentBody_Is422AndCreatesNothing()
    {
        await PrepareAsync();
        using var first = await PostProbeAsync("key-2", NewName());
        string otherName = NewName();

        using var other = await PostProbeAsync("key-2", otherName);

        other.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await other.Content.ReadFromJsonAsync<Response<object>>();
        body!.Succeeded.Should().BeFalse();
        body.Errors.Should().ContainSingle().Which.StatusCode.Should().Be(422);
        (await CountAsync("SELECT count(*) FROM gss.idempotency_probe WHERE name = @v", otherName)).Should().Be(0);
    }

    [Fact]
    public async Task SameKey_OnADifferentRoute_Is422()
    {
        await PrepareAsync();
        using var first = await PostProbeAsync("key-3", NewName());

        using var other = await Send(HttpMethod.Post, $"{IdempotencyProbeController.Path}/batch", "key-3", JsonContent.Create(new[] { new ProbeCreateDto { Name = NewName() } }));

        other.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task FailedRequest_IsNotStored_SoTheKeyCanBeReusedWithACorrectedBody()
    {
        await PrepareAsync();
        string taken = NewName();
        using (await PostProbeAsync(null, taken)) { }

        using var conflict = await PostProbeAsync("key-4", taken);
        (await CountAsync("SELECT count(*) FROM gss.idempotencykey")).Should().Be(0, "an error is rolled back and frees the key");
        using var fixedRequest = await PostProbeAsync("key-4", NewName());

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        fixedRequest.StatusCode.Should().Be(HttpStatusCode.Created);
        fixedRequest.Headers.Contains(IdempotencyLimits.ReplayedHeaderName).Should().BeFalse();
    }

    [Fact]
    public async Task ValidationError_IsNotStored()
    {
        await PrepareAsync();

        using var response = await Send(HttpMethod.Post, IdempotencyProbeController.Path, "key-5", Json("{\"name\":\"\"}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAsync("SELECT count(*) FROM gss.idempotencykey")).Should().Be(0);
    }

    [Fact]
    public async Task ParallelRequestsWithOneKey_CreateOneRow_AndEveryCallerGetsTheSameAnswer()
    {
        await PrepareAsync();
        string name = NewName();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostProbeAsync("key-6", name)));

        try
        {
            responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
            string[] bodies = [.. await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()))];
            bodies.Distinct().Should().ContainSingle("every caller sees the one created row");
            responses.Count(r => r.Headers.Contains(IdempotencyLimits.ReplayedHeaderName)).Should().Be(5);
            (await CountAsync("SELECT count(*) FROM gss.idempotency_probe WHERE name = @v", name)).Should().Be(1);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task ParallelRequestsWithOneKey_WhenTheFirstFails_TheNextOneRuns()
    {
        await PrepareAsync();
        string taken = NewName();
        using (await PostProbeAsync(null, taken)) { }

        // All of them fail with 409; none may be stored, and none may hang on the key's lock.
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostProbeAsync("key-7", taken)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await CountAsync("SELECT count(*) FROM gss.idempotencykey")).Should().Be(0);
    }

    [Fact]
    public async Task BatchCreate_Retried_ReplaysTheBatch()
    {
        await PrepareAsync();
        ProbeCreateDto[] batch = [.. Enumerable.Range(0, 3).Select(_ => new ProbeCreateDto { Name = NewName() })];

        using var first = await Send(HttpMethod.Post, $"{IdempotencyProbeController.Path}/batch", "key-8", JsonContent.Create(batch));
        using var retry = await Send(HttpMethod.Post, $"{IdempotencyProbeController.Path}/batch", "key-8", JsonContent.Create(batch));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.Headers.Contains(IdempotencyLimits.ReplayedHeaderName).Should().BeTrue();
        (await retry.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        (await CountAsync("SELECT count(*) FROM gss.idempotency_probe")).Should().Be(3);
    }

    [Fact]
    public async Task WithoutAKey_BehavesAsBefore()
    {
        await PrepareAsync();
        string name = NewName();

        using var first = await PostProbeAsync(null, name);
        using var second = await PostProbeAsync(null, name);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict, "no key, no replay: the unique name answers");
        (await CountAsync("SELECT count(*) FROM gss.idempotencykey")).Should().Be(0);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("tab\there")]
    public async Task InvalidKey_Is400(string key)
    {
        await PrepareAsync();

        using var response = await PostProbeAsync(key, NewName());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(IdempotencyLimits.HeaderName);
    }

    [Fact]
    public async Task KeyLongerThanTheLimit_Is400()
    {
        await PrepareAsync();

        using var response = await PostProbeAsync(new string('k', IdempotencyLimits.MaxKeyLength + 1), NewName());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExpiredKey_IsRunAgain_AndTheRowIsReplaced()
    {
        await PrepareAsync();
        using (await PostProbeAsync("key-9", NewName())) { }
        await ExecuteAsync("UPDATE gss.idempotencykey SET createdat = now() - interval '25 hours'");
        string newName = NewName();

        using var again = await PostProbeAsync("key-9", newName);

        again.StatusCode.Should().Be(HttpStatusCode.Created, "a key older than the retention is forgotten, whatever the body");
        again.Headers.Contains(IdempotencyLimits.ReplayedHeaderName).Should().BeFalse();
        (await CountAsync("SELECT count(*) FROM gss.idempotencykey")).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM gss.idempotencykey WHERE createdat > now() - interval '1 hour'")).Should().Be(1);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyExpiredRows_InBatches()
    {
        await PrepareAsync();
        using (await PostProbeAsync("key-a", NewName())) { }
        using (await PostProbeAsync("key-b", NewName())) { }
        await ExecuteAsync("UPDATE gss.idempotencykey SET createdat = now() - interval '25 hours' WHERE idempotencykey = 'key-a'");
        await using var scope = Factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<GSS.Core.Interfaces.IIdempotencyRepository>();

        int deleted = await repository.DeleteExpiredAsync(DateTime.UtcNow - IdempotencyLimits.Retention, batchSize: 1);

        deleted.Should().Be(1);
        (await CountAsync("SELECT count(*) FROM gss.idempotencykey WHERE idempotencykey = 'key-b'")).Should().Be(1);
    }

    /// <summary>Resets the database (starting the host and its migrations), then makes sure the probe table exists.</summary>
    private async Task PrepareAsync()
    {
        await ResetDatabaseAsync();
        await ExecuteAsync(IdempotencyProbeController.TableDdl);
    }

    private static string NewName() => $"idem-{Guid.NewGuid():N}";

    private static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    private Task<HttpResponseMessage> PostProbeAsync(string? key, string name)
    {
        return Send(HttpMethod.Post, IdempotencyProbeController.Path, key,
            JsonContent.Create(new ProbeCreateDto { Name = name }));
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? key, HttpContent content)
    {
        HttpRequestMessage request = new(method, url) { Content = content };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyLimits.HeaderName, key);
        }

        return Client.SendAsync(request);
    }

    private static async Task<long> CountAsync(string sql, string? value = null)
    {
        await using NpgsqlConnection connection = new(TestDatabaseSettings.ConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        if (value is not null)
        {
            command.Parameters.AddWithValue("v", value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = new(TestDatabaseSettings.ConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
