using System.Data;
using System.Net;
using System.Text;
using FluentAssertions;
using GSS.Core.Exceptions;
using GSS.Core.Models;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>The one send path and the MIS/ASM envelope helpers (models must be in the source-generated JsonContext).</summary>
[Trait("Category", TestCategories.Unit)]
public class HttpServiceBaseTests
{
    [Fact]
    public async Task GetListAsync_ReturnsData()
    {
        var service = Service(HttpStatusCode.OK, """{"succeeded":true,"data":[{"roleId":7}]}""");

        var items = await service.GetList();

        items.Should().ContainSingle().Which.Id.Should().Be(7);
    }

    [Fact]
    public async Task GetListAsync_NullData_ReturnsEmpty()
    {
        var items = await Service(HttpStatusCode.OK, """{"succeeded":true,"data":null}""").GetList();

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetListAsync_NoBody_Throws502NeverEmpty()
    {
        Func<Task> act = () => Service(HttpStatusCode.OK, "").GetList();

        (await act.Should().ThrowAsync<ExternalServiceException>())
            .Which.Failure.Should().Be(ExternalServiceFailure.Failed);
    }

    [Fact]
    public async Task GetListAsync_SucceededFalse_Throws502WithTheUpstreamsFirstError()
    {
        Func<Task> act = () => Service(HttpStatusCode.OK,
            """{"succeeded":false,"errors":[{"errorId":"E1","statusCode":500,"message":"lookup failed"}]}""").GetList();

        var ex = (await act.Should().ThrowAsync<ExternalServiceException>()).Which;
        ex.Failure.Should().Be(ExternalServiceFailure.Failed);
        ex.Message.Should().Be("Test GET items failed: Succeeded=false (E1-500-lookup failed).");
    }

    [Fact]
    public async Task GetSingleOrNullAsync_Upstream404_ReturnsNull()
    {
        var item = await Service(HttpStatusCode.NotFound, "{}").GetSingle(7);

        item.Should().BeNull();
    }

    [Fact]
    public async Task GetSingleOrNullAsync_NoMatchingRoleModel_ReturnsNull()
    {
        var item = await Service(HttpStatusCode.OK, """{"succeeded":true,"data":[{"roleId":8}]}""").GetSingle(7);

        item.Should().BeNull(because: "the upstream's filter is never trusted");
    }

    /// <summary>Only an upstream 404 means "not found"; a failure flag on a by-id lookup still throws.</summary>
    [Fact]
    public async Task GetSingleOrNullAsync_SucceededFalse_Throws502()
    {
        Func<Task> act = () => Service(HttpStatusCode.OK, """{"succeeded":false}""").GetSingle(7);

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.Failure.Should().Be(ExternalServiceFailure.Failed);
    }

    [Fact]
    public async Task GetSingleOrNullAsync_Upstream500_Throws()
    {
        Func<Task> act = () => Service(HttpStatusCode.InternalServerError, "{}").GetSingle(7);

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PostAsync_MalformedJson_Throws502NamingPost()
    {
        Func<Task> act = () => Service(HttpStatusCode.OK, "{not json").Post();

        var ex = (await act.Should().ThrowAsync<ExternalServiceException>()).Which;
        ex.Failure.Should().Be(ExternalServiceFailure.Failed);
        ex.Message.Should().Contain(" POST items ");
    }

    [Fact]
    public async Task PostAsync_Non2xx_ThrowsWithTheStatus()
    {
        Func<Task> act = () => Service(HttpStatusCode.BadGateway, "{}").Post();

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task PostWithoutBodyAsync_SendsNoContentAndAppliesConfigure()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "");
        TestService service = new(new HttpClient(handler) { BaseAddress = new Uri("https://up.example.com/") });

        bool ok = await service.PostWithoutBody();

        ok.Should().BeTrue();
        handler.Method.Should().Be(HttpMethod.Post);
        handler.HadContent.Should().BeFalse();
        handler.Authorization.Should().Be("t");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlankEndpoint_ThrowsArgumentException(string endpoint)
    {
        Func<Task> act = () => Service(HttpStatusCode.OK, "{}").GetRaw(endpoint);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// An open transaction would hold its pooled connection and row locks across the network call (and its retries):
    /// refused before anything is sent.
    /// </summary>
    [Fact]
    public async Task Send_InsideAnOpenDatabaseTransaction_ThrowsWithoutSending()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        Mock<IDapperTransactionManager> transactionManager = new();
        transactionManager.Setup(t => t.GetCurrentTransaction()).Returns(Mock.Of<IDbTransaction>());
        TestService service = new(new HttpClient(handler) { BaseAddress = new Uri("https://up.example.com/") }, transactionManager.Object);

        Func<Task> act = () => service.GetRaw("items");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*inside an open database transaction*");
        handler.Method.Should().BeNull("nothing was sent");
    }

    private static TestService Service(HttpStatusCode status, string body)
    {
        return new TestService(new HttpClient(new CapturingHandler(status, body)) { BaseAddress = new Uri("https://up.example.com/") });
    }

    private sealed class TestService(HttpClient httpClient, IDapperTransactionManager? transactionManager = null)
        : HttpServiceBase(httpClient, transactionManager ?? Mock.Of<IDapperTransactionManager>(), NullLogger.Instance, "Test")
    {
        public Task<IReadOnlyList<RoleModel>> GetList() => GetListAsync<RoleModel>("items", null);

        public Task<RoleModel?> GetSingle(int id) => GetSingleOrNullAsync<RoleModel>("items", item => item.Id == id, null);

        public Task<RoleModel?> GetRaw(string endpoint) => GetAsync<RoleModel>(endpoint, null);

        public Task<SsoAuthResponse?> Post() => PostAsync<RenewTokenRequest, SsoAuthResponse>(
            "items", new RenewTokenRequest { RefreshToken = "t" }, null);

        public Task<bool> PostWithoutBody() => PostWithoutBodyAsync("logout", request => request.SetBearerToken("t"));
    }

    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public bool HadContent { get; private set; }

        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            HadContent = request.Content != null;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
