using Caching.NET;
using Caching.NET.Extensions;
using Caching.NET.Options;
using FluentAssertions;
using GSS.Business.DTOs;
using GSS.Business.Services;
using GSS.Core.Exceptions;
using GSS.Core.Helpers;
using GSS.UnitTests.Common;
using GSS.Util.Security;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GSS.UnitTests.Business.Services;

/// <summary>
/// Unit tests for SsoService.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class SsoServiceTests
{
    private readonly Mock<Core.Interfaces.Services.ISsoService> _mockCoreService = new();
    private readonly ICacheService _cacheService = TestCache.Create();
    private readonly Mock<ILogger<SsoService>> _mockLogger = new();
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly SsoService _service;

    /// <summary>The token's exp, as the handler passes it.</summary>
    private DateTimeOffset Expiry => _clock.GetUtcNow().AddHours(1);

    public SsoServiceTests()
    {
        _service = new SsoService(_mockCoreService.Object, _cacheService, _clock, _mockLogger.Object);
    }

    #region ValidateTokenAsync Tests

    [Fact]
    public async Task ValidateTokenAsync_CachesUntilTheGivenExpiry_WithoutReadingTheToken()
    {
        // The handler already parsed exp; an opaque token with an expiry is still cached.
        const string token = "opaque-token";
        _mockCoreService.Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _service.ValidateTokenAsync(token, _clock.GetUtcNow().AddHours(1));
        await _service.ValidateTokenAsync(token, _clock.GetUtcNow().AddHours(1));

        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateTokenAsync_NoExpiryGiven_IsNotCached_EvenIfTheTokenHasExp()
    {
        string token = CreateToken();
        _mockCoreService.Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _service.ValidateTokenAsync(token, tokenExpiresAtUtc: null);
        await _service.ValidateTokenAsync(token, tokenExpiresAtUtc: null);

        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsTrue()
    {
        // Arrange
        string token = CreateToken();
        _mockCoreService
            .Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        bool result = await _service.ValidateTokenAsync(token, Expiry);

        // Assert
        result.Should().BeTrue();
        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_ReturnsFalse()
    {
        // Arrange
        string token = CreateToken();
        _mockCoreService
            .Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        bool result = await _service.ValidateTokenAsync(token, Expiry);

        // Assert
        result.Should().BeFalse();
        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        // Logged once, by the authentication handler.
        _mockLogger.Verify(
            l => l.Log(
                It.Is<LogLevel>(level => level >= LogLevel.Information),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_RepeatWithinItsLifetime_SharesOneSsoCall()
    {
        string token = CreateToken();
        _mockCoreService.Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        bool first = await _service.ValidateTokenAsync(token, Expiry);
        bool second = await _service.ValidateTokenAsync(token, Expiry);

        first.Should().BeFalse();
        second.Should().BeFalse();
        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    // The lifetimes below are what the service asks the cache for, computed from the fake clock. Waiting for a real
    // entry to expire would need the cache's own (real) clock, which Caching.NET does not let a test replace.

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_IsCachedUntilTheTokenExpires()
    {
        (var requested, var chosen) = await CaptureLifetimesAsync(ssoAnswer: true, TimeSpan.FromHours(1));

        requested!.LocalExpiration.Should().Be(TimeSpan.FromHours(1));
        requested.DistributedExpiration.Should().Be(TimeSpan.FromHours(1));
        requested.FailSafe.Should().BeFalse("authentication data is never served stale");
        chosen.LocalExpiration.Should().BeNull("a valid result keeps the token's lifetime");
        chosen.DistributedExpiration.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_IsCachedForFiveSeconds()
    {
        (_, var chosen) = await CaptureLifetimesAsync(ssoAnswer: false, TimeSpan.FromHours(1));

        chosen.LocalExpiration.Should().Be(TimeSpan.FromSeconds(5));
        chosen.DistributedExpiration.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_IsNeverCachedPastTheTokenExpiry()
    {
        (_, var chosen) = await CaptureLifetimesAsync(ssoAnswer: false, TimeSpan.FromSeconds(1.5));

        chosen.LocalExpiration.Should().Be(TimeSpan.FromSeconds(1.5));
        chosen.DistributedExpiration.Should().Be(TimeSpan.FromSeconds(1.5));
    }

    /// <summary>
    /// Validates a token with <paramref name="remaining"/> left on the fake clock and returns the overrides the service
    /// passed to the cache and those its factory set from SSO's answer.
    /// </summary>
    private async Task<(CacheEntryOverrides? Requested, CacheEntryOverrides Chosen)> CaptureLifetimesAsync(bool ssoAnswer, TimeSpan remaining)
    {
        string token = CreateToken();
        _mockCoreService.Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(ssoAnswer);
        CacheEntryOverrides? requested = null;
        CacheEntryOverrides? chosen = null;
        // Forwards to the real cache, recording the options and the factory context's overrides.
        Mock<ICacheService> capturingCache = new();
        capturingCache
            .Setup(c => c.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CacheFactoryContext<bool>, CancellationToken, Task<bool>>>(),
                It.IsAny<CacheValue<bool>>(),
                It.IsAny<CacheEntryOverrides?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<CacheFactoryContext<bool>, CancellationToken, Task<bool>> factory, CacheValue<bool> failSafeDefault,
                CacheEntryOverrides? options, IEnumerable<string>? tags, CancellationToken ct) =>
            {
                requested = options;
                return _cacheService.GetOrSetAsync<bool>(key, async (ctx, cancel) =>
                {
                    bool value = await factory(ctx, cancel);
                    chosen = ctx.Overrides;
                    return value;
                }, failSafeDefault, options, tags, ct);
            });
        SsoService capturing = new(_mockCoreService.Object, capturingCache.Object, _clock, _mockLogger.Object);

        bool result = await capturing.ValidateTokenAsync(token, _clock.GetUtcNow().Add(remaining));

        result.Should().Be(ssoAnswer);
        return (requested, chosen!);
    }

    [Fact]
    public async Task ValidateTokenAsync_ConcurrentFirstRequests_ShareOneSsoCall()
    {
        // Arrange: SSO answers only after every request has started
        string token = CreateToken();
        TaskCompletionSource<bool> ssoAnswer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockCoreService
            .Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()))
            .Returns(ssoAnswer.Task);

        // Act
        var requests = Enumerable.Range(0, 10).Select(_ => _service.ValidateTokenAsync(token, Expiry)).ToArray();
        await Task.Delay(100);
        ssoAnswer.SetResult(true);
        bool[] results = await Task.WhenAll(requests);

        // Assert
        results.Should().OnlyContain(r => r);
        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_SecondCallServedFromCache()
    {
        // Arrange
        string token = CreateToken();
        _mockCoreService
            .Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        bool first = await _service.ValidateTokenAsync(token, Expiry);
        bool second = await _service.ValidateTokenAsync(token, Expiry);

        // Assert
        first.Should().BeTrue();
        second.Should().BeTrue();
        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateTokenAsync_NullOrEmptyToken_ReturnsFalse()
    {
        // Arrange
        const string? token = null;

        // Act
        bool result = await _service.ValidateTokenAsync(token!, Expiry);

        // Assert
        result.Should().BeFalse();
        _mockCoreService.Verify(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateTokenAsync_WhitespaceToken_ReturnsFalse()
    {
        // Arrange
        const string token = "   ";

        // Act
        bool result = await _service.ValidateTokenAsync(token, Expiry);

        // Assert
        result.Should().BeFalse();
        _mockCoreService.Verify(s => s.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateTokenAsync_InternalError_PropagatesAndCachesNothing()
    {
        // A bug (e.g. a bad SSO payload) is not a verdict on the token: never a cached rejection (401 logs users out).
        string token = CreateToken();
        _mockCoreService
            .SetupSequence(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Text.Json.JsonException("bad payload"))
            .ReturnsAsync(true);

        Func<Task> act = () => _service.ValidateTokenAsync(token, Expiry);

        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
        (await _service.ValidateTokenAsync(token, Expiry)).Should().BeTrue();
        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ValidateTokenAsync_SsoOutage_PropagatesAndCachesNothing()
    {
        // An outage is not a verdict on the token: the caller answers 503, not 401.
        string token = CreateToken();
        _mockCoreService
            .SetupSequence(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("SsoService", ExternalServiceFailure.Unavailable, "circuit open"))
            .ReturnsAsync(false);

        Func<Task> act = () => _service.ValidateTokenAsync(token, Expiry);

        await act.Should().ThrowAsync<ExternalServiceException>();
        (await _service.ValidateTokenAsync(token, Expiry)).Should().BeFalse();
        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    #endregion

    #region RenewTokenAsync Tests

    [Fact]
    public async Task RenewTokenAsync_ValidTokens_ReturnsAuthResponse()
    {
        // Arrange
        const string accessToken = "access-token";
        const string refreshToken = "refresh-token";
        Core.Models.SsoAuthResponse coreResponse = new Core.Models.SsoAuthResponse
        {
            AccessToken = "new-access-token",
            RefreshToken = "new-refresh-token"
        };

        _mockCoreService
            .Setup(s => s.RenewTokenAsync(accessToken, refreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coreResponse);

        // Act
        var result = await _service.RenewTokenAsync(accessToken, refreshToken);

        // Assert
        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("new-access-token");
        result.RefreshToken.Should().Be("new-refresh-token");
        _mockCoreService.Verify(s => s.RenewTokenAsync(accessToken, refreshToken, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenewTokenAsync_InvalidTokens_ReturnsNull()
    {
        // Arrange
        const string accessToken = "invalid-access-token";
        const string refreshToken = "invalid-refresh-token";

        _mockCoreService
            .Setup(s => s.RenewTokenAsync(accessToken, refreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Core.Models.SsoAuthResponse?)null);

        // Act
        var result = await _service.RenewTokenAsync(accessToken, refreshToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task RenewTokenAsync_NullAccessToken_ThrowsArgumentException()
    {
        // Arrange
        const string? accessToken = null;
        const string refreshToken = "refresh-token";

        // Act
        Func<Task> act = async () => await _service.RenewTokenAsync(accessToken!, refreshToken);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RenewTokenAsync_NullRefreshToken_ThrowsArgumentException()
    {
        // Arrange
        const string accessToken = "access-token";
        const string? refreshToken = null;

        // Act
        Func<Task> act = async () => await _service.RenewTokenAsync(accessToken, refreshToken!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region LogoutAsync Tests

    [Fact]
    public async Task LogoutAsync_ValidToken_ReturnsTrue()
    {
        // Arrange
        string token = CreateToken();

        _mockCoreService
            .Setup(s => s.LogoutAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        bool result = await _service.LogoutAsync(token, null);

        // Assert
        result.Should().BeTrue();
        _mockCoreService.Verify(s => s.LogoutAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_ValidToken_InvalidatesCachedValidation()
    {
        // Arrange - populate the validation cache
        string token = CreateToken();
        _mockCoreService
            .Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCoreService
            .Setup(s => s.LogoutAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await _service.ValidateTokenAsync(token, Expiry);

        // Act
        await _service.LogoutAsync(token, null);
        await _service.ValidateTokenAsync(token, Expiry);

        // Assert - logout dropped the cached validation, so SSO is called again
        _mockCoreService.Verify(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task LogoutAsync_ClientCancelsAfterSsoLoggedOut_StillClearsCache()
    {
        // An SPA logs out and navigates away: SSO ended the session, then the request is aborted.
        string token = CreateToken();
        using CancellationTokenSource request = new();
        _mockCoreService
            .Setup(s => s.LogoutAsync(token, It.IsAny<CancellationToken>()))
            .Returns(() => ClientDisconnects<bool>(request));
        await SeedLogoutEntriesAsync(token);

        Func<Task> act = () => _service.LogoutAsync(token, "42", request.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await AssertLogoutEntriesClearedAsync(token);
    }

    private async Task SeedLogoutEntriesAsync(string token)
    {
        await _cacheService.SetAsync(CacheKeys.TokenValidation(token), true);
        await _cacheService.SetAsync(CacheKeys.AsmSecurity(token), "asm");
        await _cacheService.SetAsync(CacheKeys.PersonPositions("42"), "positions");
    }

    private async Task AssertLogoutEntriesClearedAsync(string token)
    {
        (await _cacheService.ExistsAsync<bool>(CacheKeys.TokenValidation(token))).Should().BeFalse();
        (await _cacheService.ExistsAsync<string>(CacheKeys.AsmSecurity(token))).Should().BeFalse();
        (await _cacheService.ExistsAsync<string>(CacheKeys.PersonPositions("42"))).Should().BeFalse();
    }

    /// <summary>SSO acted, then the client disconnected: the request token is cancelled and the call throws.</summary>
    private static async Task<T> ClientDisconnects<T>(CancellationTokenSource request)
    {
        await request.CancelAsync();
        throw new OperationCanceledException(request.Token);
    }

    [Fact]
    public async Task LogoutAsync_InvalidToken_ReturnsFalse()
    {
        // Arrange
        string token = CreateToken();

        _mockCoreService
            .Setup(s => s.LogoutAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        bool result = await _service.LogoutAsync(token, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task LogoutAsync_NullToken_ReturnsFalse()
    {
        // Arrange
        const string? token = null;

        _mockCoreService
            .Setup(s => s.LogoutAsync(token!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        bool result = await _service.LogoutAsync(token!, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task LogoutAsync_EmptyToken_ReturnsFalse()
    {
        // Arrange
        const string token = "";

        _mockCoreService
            .Setup(s => s.LogoutAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        bool result = await _service.LogoutAsync(token, null);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Renew eviction Tests

    [Fact]
    public async Task RenewTokenAsync_Success_EvictsOldTokenEntries()
    {
        string oldToken = CreateToken();
        SetupRenew(oldToken, NewTokens());
        await _cacheService.SetAsync(CacheKeys.TokenValidation(oldToken), true);
        await _cacheService.SetAsync(CacheKeys.AsmSecurity(oldToken), "asm");
        await _cacheService.SetAsync(CacheKeys.PersonPositions("42"), "positions");

        var result = await _service.RenewTokenAsync(oldToken, "refresh-token");

        result.Should().NotBeNull();
        (await _cacheService.ExistsAsync<bool>(CacheKeys.TokenValidation(oldToken))).Should().BeFalse();
        (await _cacheService.ExistsAsync<string>(CacheKeys.AsmSecurity(oldToken))).Should().BeFalse();
        (await _cacheService.ExistsAsync<string>(CacheKeys.PersonPositions("42"))).Should().BeTrue(because: "positions belong to the user, not the token");
    }

    [Fact]
    public async Task RenewTokenAsync_Success_OldTokenIsRevalidatedWithSso()
    {
        string oldToken = CreateToken();
        _mockCoreService.Setup(s => s.ValidateTokenAsync(oldToken, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        SetupRenew(oldToken, NewTokens());

        await _service.ValidateTokenAsync(oldToken, Expiry);
        await _service.RenewTokenAsync(oldToken, "refresh-token");
        await _service.ValidateTokenAsync(oldToken, Expiry);

        _mockCoreService.Verify(s => s.ValidateTokenAsync(oldToken, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RenewTokenAsync_SsoRejects_KeepsOldTokenEntries()
    {
        string oldToken = CreateToken();
        SetupRenew(oldToken, null);
        await _cacheService.SetAsync(CacheKeys.TokenValidation(oldToken), true);

        var result = await _service.RenewTokenAsync(oldToken, "refresh-token");

        result.Should().BeNull();
        (await _cacheService.ExistsAsync<bool>(CacheKeys.TokenValidation(oldToken))).Should().BeTrue();
    }

    [Fact]
    public async Task RenewTokenAsync_EvictionThrows_IsNotSwallowed()
    {
        // Caching.NET already absorbs cache-infrastructure failures (CachingConfigurationTests); anything it throws is a bug.
        string oldToken = CreateToken();
        SetupRenew(oldToken, NewTokens());
        SsoService sut = new(_mockCoreService.Object, ThrowingCache(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CacheEntryOverrides?>(), It.IsAny<CancellationToken>())), _clock, _mockLogger.Object);

        Func<Task> act = () => sut.RenewTokenAsync(oldToken, "refresh-token");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RenewTokenAsync_ClientCancelsAfterSsoRenewed_StillEvictsOldToken()
    {
        string oldToken = CreateToken();
        using CancellationTokenSource request = new();
        _mockCoreService
            .Setup(s => s.RenewTokenAsync(oldToken, "refresh-token", It.IsAny<CancellationToken>()))
            .Returns(() => ClientDisconnects<Core.Models.SsoAuthResponse?>(request));
        await _cacheService.SetAsync(CacheKeys.TokenValidation(oldToken), true);
        await _cacheService.SetAsync(CacheKeys.AsmSecurity(oldToken), "asm");

        Func<Task> act = () => _service.RenewTokenAsync(oldToken, "refresh-token", request.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await _cacheService.ExistsAsync<bool>(CacheKeys.TokenValidation(oldToken))).Should().BeFalse();
        (await _cacheService.ExistsAsync<string>(CacheKeys.AsmSecurity(oldToken))).Should().BeFalse();
    }

    [Fact]
    public async Task RenewTokenAsync_SsoOutage_KeepsOldTokenEntries()
    {
        string oldToken = CreateToken();
        _mockCoreService
            .Setup(s => s.RenewTokenAsync(oldToken, "refresh-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("SsoService", ExternalServiceFailure.Unavailable, "circuit open"));
        await _cacheService.SetAsync(CacheKeys.TokenValidation(oldToken), true);

        Func<Task> act = () => _service.RenewTokenAsync(oldToken, "refresh-token");

        await act.Should().ThrowAsync<ExternalServiceException>();
        (await _cacheService.ExistsAsync<bool>(CacheKeys.TokenValidation(oldToken))).Should().BeTrue(
            because: "SSO did not renew, so the old token is still valid and its next request must not hit the outage");
    }

    private void SetupRenew(string accessToken, Core.Models.SsoAuthResponse? response)
    {
        _mockCoreService
            .Setup(s => s.RenewTokenAsync(accessToken, "refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    private static Core.Models.SsoAuthResponse NewTokens()
    {
        return new Core.Models.SsoAuthResponse { AccessToken = "new-access-token", RefreshToken = "new-refresh-token" };
    }

    #endregion

    #region Single-owner cache Tests

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_IsCachedUnderTokenValidationKey()
    {
        string token = CreateToken();
        _mockCoreService.Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _service.ValidateTokenAsync(token, Expiry);

        var cached = await _cacheService.TryGetAsync<bool>(CacheKeys.TokenValidation(token));
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTokenAsync_CacheThrows_IsNotSwallowed()
    {
        string token = CreateToken();
        _mockCoreService.Setup(s => s.ValidateTokenAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        SsoService sut = new(
            _mockCoreService.Object,
            ThrowingCache(c => c.GetOrSetAsync<bool>(
                It.IsAny<string>(),
                It.IsAny<Func<CacheFactoryContext<bool>, CancellationToken, Task<bool>>>(),
                It.IsAny<CacheValue<bool>>(),
                It.IsAny<CacheEntryOverrides?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>())),
            _clock,
            _mockLogger.Object);

        Func<Task> act = () => sut.ValidateTokenAsync(token, Expiry);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static ICacheService ThrowingCache<TResult>(System.Linq.Expressions.Expression<Func<ICacheService, ValueTask<TResult>>> call)
    {
        Mock<ICacheService> cache = new();
        cache.Setup(call).ThrowsAsync(new InvalidOperationException("cache bug"));
        return cache.Object;
    }

    private static ICacheService ThrowingCache(System.Linq.Expressions.Expression<Func<ICacheService, ValueTask>> call)
    {
        Mock<ICacheService> cache = new();
        cache.Setup(call).ThrowsAsync(new InvalidOperationException("cache bug"));
        return cache.Object;
    }

    [Fact]
    public async Task LogoutAsync_Success_RemovesValidationAsmAndPositionEntries()
    {
        string token = CreateToken();
        const string userId = "42";
        _mockCoreService.Setup(s => s.LogoutAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await _cacheService.SetAsync(CacheKeys.TokenValidation(token), true);
        await _cacheService.SetAsync(CacheKeys.AsmSecurity(token), "asm");
        await _cacheService.SetAsync(CacheKeys.PersonPositions(userId), "positions");

        bool result = await _service.LogoutAsync(token, userId);

        result.Should().BeTrue();
        (await _cacheService.ExistsAsync<bool>(CacheKeys.TokenValidation(token))).Should().BeFalse();
        (await _cacheService.ExistsAsync<string>(CacheKeys.AsmSecurity(token))).Should().BeFalse();
        (await _cacheService.ExistsAsync<string>(CacheKeys.PersonPositions(userId))).Should().BeFalse();
    }

    [Fact]
    public async Task LogoutAsync_WithoutUserId_KeepsPositionEntries()
    {
        string token = CreateToken();
        _mockCoreService.Setup(s => s.LogoutAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await _cacheService.SetAsync(CacheKeys.PersonPositions("42"), "positions");

        await _service.LogoutAsync(token, null);

        (await _cacheService.ExistsAsync<string>(CacheKeys.PersonPositions("42"))).Should().BeTrue();
    }

    /// <summary>SSO no longer accepts the token, so it must not stay trusted from cache until its exp.</summary>
    [Fact]
    public async Task LogoutAsync_SsoRejects_ReturnsFalseAndStillClearsCachedEntries()
    {
        string token = CreateToken();
        const string userId = "42";
        _mockCoreService.Setup(s => s.LogoutAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await _cacheService.SetAsync(CacheKeys.TokenValidation(token), true);
        await _cacheService.SetAsync(CacheKeys.AsmSecurity(token), "asm");
        await _cacheService.SetAsync(CacheKeys.PersonPositions(userId), "positions");

        bool result = await _service.LogoutAsync(token, userId);

        result.Should().BeFalse();
        (await _cacheService.ExistsAsync<bool>(CacheKeys.TokenValidation(token))).Should().BeFalse();
        (await _cacheService.ExistsAsync<string>(CacheKeys.AsmSecurity(token))).Should().BeFalse();
        (await _cacheService.ExistsAsync<string>(CacheKeys.PersonPositions(userId))).Should().BeFalse();
    }

    /// <summary>An outage is not an answer about the token: nothing is cleared and the outage propagates.</summary>
    [Fact]
    public async Task LogoutAsync_SsoOutage_PropagatesAndKeepsCachedEntries()
    {
        string token = CreateToken();
        _mockCoreService
            .Setup(s => s.LogoutAsync(token, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("SsoService", ExternalServiceFailure.Unavailable, "circuit open"));
        await _cacheService.SetAsync(CacheKeys.TokenValidation(token), true);

        Func<Task> act = () => _service.LogoutAsync(token, "42");

        await act.Should().ThrowAsync<ExternalServiceException>();
        (await _cacheService.ExistsAsync<bool>(CacheKeys.TokenValidation(token))).Should().BeTrue();
    }

    [Fact]
    public async Task LogoutAsync_BlankToken_ReturnsFalseWithoutCallingSso()
    {
        bool result = await _service.LogoutAsync("  ", "42");

        result.Should().BeFalse();
        _mockCoreService.Verify(s => s.LogoutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    private static string CreateToken()
    {
        var expires = DateTime.UtcNow.AddHours(1);
        return TestJwt.CreateWithFakeSignature(
            new Dictionary<string, object> { [JwtTokenHelper.ClaimTypeUserId] = "42", ["jti"] = Guid.NewGuid().ToString() },
            expires: expires,
            notBefore: expires.AddHours(-2));
    }
}
