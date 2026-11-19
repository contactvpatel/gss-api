using System.Diagnostics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GSS.Api.Authentication;
using GSS.Core.Exceptions;
using GSS.Infrastructure.Services.Internal;
using GSS.UnitTests.Common;
using GSS.Util.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ISsoService = GSS.Business.Services.Interfaces.ISsoService;

namespace GSS.UnitTests.API.Authentication;

[Trait("Category", TestCategories.Unit)]
public class SsoAuthenticationHandlerTests
{
    private readonly Mock<ISsoService> _mockSsoService = new();

    [Fact]
    public async Task Authenticate_NoAuthorizationHeader_ReturnsNoResult()
    {
        (var handler, _) = await CreateHandlerAsync(authorization: null);

        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue();
        _mockSsoService.Verify(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_MalformedToken_Fails()
    {
        (var handler, _) = await CreateHandlerAsync("Bearer not-a-jwt");

        var result = await handler.AuthenticateAsync();

        result.Failure!.Message.Should().Be("Invalid token format");
    }

    [Fact]
    public async Task Authenticate_ExpiredToken_FailsWithoutCallingSso()
    {
        (var handler, _) = await CreateHandlerAsync($"Bearer {CreateToken("u1", DateTime.UtcNow.AddHours(-1))}");

        var result = await handler.AuthenticateAsync();

        result.Failure!.Message.Should().Be("Token has expired");
        _mockSsoService.Verify(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_ValidToken_ReturnsPrincipalAndSetsItems()
    {
        string token = CreateToken("42");
        SetupSso(true);
        (var handler, var context) = await CreateHandlerAsync($"Bearer {token}");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        var user = result.Principal!;
        user.Identity!.AuthenticationType.Should().Be(SsoAuthenticationDefaults.AuthenticationScheme);
        user.Identity.Name.Should().Be("42");
        user.FindFirst(JwtTokenHelper.ClaimTypeUserId)!.Value.Should().Be("42");
        user.Claims.Should().NotContain(c => c.Value == token, because: "the raw token must never become a claim");
        context.Items.Should().ContainSingle().Which.Key.Should().Be(HttpContextItemKeys.UserToken);

        // IUserContext (Infrastructure) reads what the handler stored.
        context.User = user;
        UserContext userContext = new(new HttpContextAccessor { HttpContext = context });
        userContext.GetUserId().Should().Be("42");
        userContext.GetToken().Should().Be(token);
    }

    [Fact]
    public async Task Authenticate_ValidToken_TagsTheRequestSpanWithTheUserId()
    {
        SetupSso(true);
        (var handler, var context) = await CreateHandlerAsync($"Bearer {CreateToken("42")}");
        using Activity requestActivity = new("request");
        context.Features.Set(Mock.Of<IHttpActivityFeature>(f => f.Activity == requestActivity));

        await handler.AuthenticateAsync();

        requestActivity.GetTagItem("user.id").Should().Be("42");
    }

    [Fact]
    public async Task Authenticate_TokenRejectedBySso_DoesNotTagTheRequestSpan()
    {
        SetupSso(false);
        (var handler, var context) = await CreateHandlerAsync($"Bearer {CreateToken("42")}");
        using Activity requestActivity = new("request");
        context.Features.Set(Mock.Of<IHttpActivityFeature>(f => f.Activity == requestActivity));

        await handler.AuthenticateAsync();

        requestActivity.GetTagItem("user.id").Should().BeNull();
    }

    [Fact]
    public async Task Authenticate_PassesTheParsedExpiryToSso_SoTheTokenIsParsedOnce()
    {
        DateTime expires = new(2099, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        string token = CreateToken("42", expires);
        SetupSso(true);
        (var handler, _) = await CreateHandlerAsync($"Bearer {token}");

        await handler.AuthenticateAsync();

        _mockSsoService.Verify(s => s.ValidateTokenAsync(token, new DateTimeOffset(expires), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Authenticate_TokenRejectedBySso_FailsAndSetsNoItems()
    {
        SetupSso(false);
        (var handler, var context) = await CreateHandlerAsync($"Bearer {CreateToken("42")}");

        var result = await handler.AuthenticateAsync();

        result.Failure!.Message.Should().Be("Token validation failed");
        context.Items.Should().BeEmpty();
    }

    /// <summary>The handler is the boundary that logs a rejected token, once.</summary>
    [Fact]
    public async Task Authenticate_TokenRejectedBySso_LogsOneWarning()
    {
        SetupSso(false);
        Mock<ILogger> logger = new();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerFactory> loggerFactory = new();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
        (var handler, _) = await CreateHandlerAsync($"Bearer {CreateToken("42")}", loggerFactory: loggerFactory.Object);

        await handler.AuthenticateAsync();

        logger.Verify(
            l => l.Log(
                It.Is<LogLevel>(level => level >= LogLevel.Warning),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.Is<EventId>(e => e.Id == 2101),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// A token rejected locally is the client's mistake and runs before rate limiting, so it logs Information, never a
    /// Warning a script could flood.
    /// </summary>
    [Theory]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("no-expiry")]
    [InlineData("non-numeric-user")]
    public async Task Authenticate_TokenRejectedLocally_LogsInformationNotWarning(string kind)
    {
        string token = kind switch
        {
            "malformed" => "not-a-jwt",
            "expired" => CreateToken("42", DateTime.UtcNow.AddHours(-1)),
            "no-expiry" => CreateTokenWithoutExpiry("42"),
            _ => CreateToken("abc")
        };
        Mock<ILogger> logger = new();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerFactory> loggerFactory = new();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
        (var handler, _) = await CreateHandlerAsync($"Bearer {token}", loggerFactory: loggerFactory.Object);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 2100),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        logger.Verify(
            l => l.Log(
                It.Is<LogLevel>(level => level >= LogLevel.Warning),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
        _mockSsoService.Verify(
            s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_EveryRequest_DelegatesValidationToSsoService()
    {
        // Caching belongs to the SSO service; the handler keeps no cache of its own.
        string token = CreateToken("42");
        SetupSso(true);

        (var first, _) = await CreateHandlerAsync($"Bearer {token}");
        (var second, _) = await CreateHandlerAsync($"Bearer {token}");
        await first.AuthenticateAsync();
        var result = await second.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        _mockSsoService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Authenticate_InternalError_FailsClosedWithTheError()
    {
        SetupSsoError();
        (var handler, var context) = await CreateHandlerAsync($"Bearer {CreateToken("42")}");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().BeOfType<InvalidOperationException>();
        context.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Challenge_InternalError_Writes500_Not401()
    {
        SetupSsoError();
        (var handler, var context) = await CreateHandlerAsync($"Bearer {CreateToken("42")}");

        await handler.ChallengeAsync(new AuthenticationProperties());

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.Headers.WWWAuthenticate.ToString().Should().BeEmpty("a 401 challenge would make clients log the user out");
        string body = await ReadBodyAsync(context);
        body.Should().Contain("\"succeeded\":false").And.NotContain("config bug", "exception messages are never returned");
    }

    [Fact]
    public async Task Authenticate_TokenWithEmptySignatureSegment_FailsWithoutCallingSso()
    {
        string unsigned = CreateToken("42")[..^"test-signature".Length];
        (var handler, _) = await CreateHandlerAsync($"Bearer {unsigned}");

        var result = await handler.AuthenticateAsync();

        result.Failure!.Message.Should().Be("Invalid token format");
        _mockSsoService.Verify(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Exactly three non-empty dot-separated parts, checked before parsing.</summary>
    [Theory]
    [InlineData("a.b")]
    [InlineData(".b.c")]
    [InlineData("a..c")]
    [InlineData("a.b.")]
    [InlineData("a.b.c.d")]
    [InlineData("..")]
    public async Task Authenticate_TokenWithoutThreeNonEmptyParts_FailsWithoutCallingSso(string token)
    {
        (var handler, _) = await CreateHandlerAsync($"Bearer {token}");

        var result = await handler.AuthenticateAsync();

        result.Failure!.Message.Should().Be("Invalid token format");
        _mockSsoService.Verify(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_TokenWithoutExpiry_FailsWithoutCallingSso()
    {
        (var handler, _) = await CreateHandlerAsync($"Bearer {CreateTokenWithoutExpiry("42")}");

        var result = await handler.AuthenticateAsync();

        result.Failure!.Message.Should().Be("Token has no expiry");
        _mockSsoService.Verify(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_NonNumericPersonId_FailsWithoutCallingSso()
    {
        (var handler, _) = await CreateHandlerAsync($"Bearer {CreateToken("not-a-number")}");

        var result = await handler.AuthenticateAsync();

        result.Failure!.Message.Should().Be("Invalid token format");
        _mockSsoService.Verify(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_TokenWithoutPersonId_FailsWithoutCallingSso()
    {
        string token = TestJwt.CreateWithFakeSignature(
            new Dictionary<string, object> { ["sub"] = "someone" },
            expires: DateTime.UtcNow.AddHours(1));
        (var handler, _) = await CreateHandlerAsync($"Bearer {token}");

        var result = await handler.AuthenticateAsync();

        result.Failure!.Message.Should().Be("Token has no user id");
        _mockSsoService.Verify(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_UsesSchemeTimeProviderForExpiry()
    {
        var expiry = DateTime.UtcNow.AddHours(1);
        TimeProvider later = new FixedTimeProvider(new DateTimeOffset(expiry.AddMinutes(1), TimeSpan.Zero));
        (var handler, _) = await CreateHandlerAsync($"Bearer {CreateToken("42", expiry)}", timeProvider: later);

        var result = await handler.AuthenticateAsync();

        result.Failure!.Message.Should().Be("Token has expired");
    }

    [Fact]
    public async Task Authenticate_Cancelled_PropagatesInsteadOfFailing()
    {
        _mockSsoService
            .Setup(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        (var handler, _) = await CreateHandlerAsync($"Bearer {CreateToken("42")}");

        Func<Task> act = () => handler.AuthenticateAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Challenge_WritesBearer401WithFailureReason()
    {
        SetupSso(false);
        (var handler, var context) = await CreateHandlerAsync($"Bearer {CreateToken("42")}");

        await handler.ChallengeAsync(new AuthenticationProperties());

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Be("Bearer");
        (await ReadBodyAsync(context)).Should().Contain("Token validation failed").And.Contain("\"succeeded\":false");
    }

    [Fact]
    public async Task Authenticate_SsoOutage_FailsWithTheOutage()
    {
        SetupSsoOutage(retryAfter: null);
        (var handler, _) = await CreateHandlerAsync($"Bearer {CreateToken("42")}");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().BeOfType<ExternalServiceException>();
    }

    [Fact]
    public async Task Challenge_SsoOutage_Writes503WithRetryAfter_Not401()
    {
        SetupSsoOutage(retryAfter: TimeSpan.FromSeconds(2.5));
        (var handler, var context) = await CreateHandlerAsync($"Bearer {CreateToken("42")}");

        await handler.ChallengeAsync(new AuthenticationProperties());

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("3");
        context.Response.Headers.WWWAuthenticate.ToString().Should().BeEmpty();
        (await ReadBodyAsync(context)).Should().Contain("temporarily unavailable").And.Contain("\"succeeded\":false");
    }

    [Fact]
    public async Task Forbid_Writes403()
    {
        (var handler, var context) = await CreateHandlerAsync(authorization: null);

        await handler.ForbidAsync(new AuthenticationProperties());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await ReadBodyAsync(context)).Should().Contain("Access denied");
    }

    private void SetupSsoOutage(TimeSpan? retryAfter)
    {
        _mockSsoService
            .Setup(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("SsoService", ExternalServiceFailure.Unavailable, "circuit open") { RetryAfter = retryAfter });
    }

    private void SetupSsoError()
    {
        _mockSsoService
            .Setup(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("config bug"));
    }

    private void SetupSso(bool isValid)
    {
        _mockSsoService
            .Setup(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(isValid);
    }

    private async Task<(SsoAuthenticationHandler Handler, HttpContext Context)> CreateHandlerAsync(
        string? authorization,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        Mock<IOptionsMonitor<AuthenticationSchemeOptions>> options = new();
        options.Setup(o => o.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions { TimeProvider = timeProvider });

        SsoAuthenticationHandler handler = new(
            options.Object, loggerFactory ?? NullLoggerFactory.Instance, UrlEncoder.Default, _mockSsoService.Object);

        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        if (authorization != null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        AuthenticationScheme scheme = new(SsoAuthenticationDefaults.AuthenticationScheme, null, typeof(SsoAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return (handler, context);
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private static string CreateTokenWithoutExpiry(string userId)
    {
        return TestJwt.CreateWithFakeSignature(new Dictionary<string, object> { [JwtTokenHelper.ClaimTypeUserId] = userId });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string CreateToken(string userId, DateTime? expires = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        return TestJwt.CreateWithFakeSignature(
            new Dictionary<string, object> { [JwtTokenHelper.ClaimTypeUserId] = userId },
            expires: expiry,
            notBefore: expiry.AddHours(-2),
            issuer: "test",
            audience: "test");
    }
}
