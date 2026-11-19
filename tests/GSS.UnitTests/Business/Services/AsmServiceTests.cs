using System.Collections.Immutable;
using Caching.NET;
using Caching.NET.Options;
using FluentAssertions;
using GSS.Business.DTOs;
using GSS.Business.Services;
using GSS.Core.Exceptions;
using GSS.Core.Models;
using GSS.UnitTests.Common;
using Moq;
using Xunit;

namespace GSS.UnitTests.Business.Services;

/// <summary>
/// Unit tests for AsmService.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class AsmServiceTests
{
    private readonly Mock<Core.Interfaces.Services.IAsmService> _mockCoreService = new();

    private readonly AsmService _service;

    /// <summary>The token's exp, as callers pass it.</summary>
    private static DateTimeOffset Expiry => DateTimeOffset.UtcNow.AddHours(1);

    public AsmServiceTests()
    {
        _service = new AsmService(_mockCoreService.Object, TestCache.Create(), TimeProvider.System);
    }

    #region GetApplicationSecurityAsync Tests

    // JWT with exp: permissions are cached only for the token's lifetime
    private static readonly string ValidToken = TestJwt.Create(expires: DateTime.UtcNow.AddHours(1));

    [Fact]
    public async Task GetApplicationSecurityAsync_CachesUntilTheGivenExpiry_WithoutReadingTheToken()
    {
        const string token = "opaque-token";
        _mockCoreService
            .Setup(s => s.GetApplicationSecurityAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _service.GetApplicationSecurityAsync(token, DateTimeOffset.UtcNow.AddHours(1));
        await _service.GetApplicationSecurityAsync(token, DateTimeOffset.UtcNow.AddHours(1));

        _mockCoreService.Verify(s => s.GetApplicationSecurityAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetApplicationSecurityAsync_TokenWithoutExpiry_IsNotCached()
    {
        const string tokenWithoutExp = "opaque-token";
        _mockCoreService
            .Setup(s => s.GetApplicationSecurityAsync(tokenWithoutExp, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _service.GetApplicationSecurityAsync(tokenWithoutExp, tokenExpiresAtUtc: null);
        await _service.GetApplicationSecurityAsync(tokenWithoutExp, tokenExpiresAtUtc: null);

        _mockCoreService.Verify(s => s.GetApplicationSecurityAsync(tokenWithoutExp, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetApplicationSecurityAsync_ValidToken_ReturnsSecurityInfo()
    {
        // Arrange
        string token = ValidToken;
        IReadOnlyList<AsmResponseModel> coreResult =
        [
            new AsmResponseModel
            {
                RoleId = 1,
                PositionId = 1,
                ApplicationAccess =
                [
                    new ApplicationAccessModel { ModuleCode = "app-1", ModuleName = "App 1", HasViewAccess = true },
                    new ApplicationAccessModel { ModuleCode = "app-2", ModuleName = "App 2", HasCreateAccess = true }
                ]
            }
        ];

        _mockCoreService
            .Setup(s => s.GetApplicationSecurityAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coreResult);

        // Act
        var result = await _service.GetApplicationSecurityAsync(token, Expiry);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].ApplicationAccess.Should().HaveCount(2);
        result[0].ApplicationAccess.ElementAt(0).ModuleCode.Should().Be("app-1");
        result[0].ApplicationAccess.ElementAt(1).ModuleCode.Should().Be("app-2");
        _mockCoreService.Verify(s => s.GetApplicationSecurityAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetApplicationSecurityAsync_SameToken_SecondCallServedFromCache()
    {
        // Arrange
        string token = ValidToken;
        IReadOnlyList<AsmResponseModel> coreResult =
        [
            new AsmResponseModel { RoleId = 1, PositionId = 1, ApplicationAccess = [] }
        ];

        _mockCoreService
            .Setup(s => s.GetApplicationSecurityAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coreResult);

        // Act
        await _service.GetApplicationSecurityAsync(token, Expiry);
        var result = await _service.GetApplicationSecurityAsync(token, Expiry);

        // Assert
        result.Should().HaveCount(1);
        _mockCoreService.Verify(s => s.GetApplicationSecurityAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>ASM's JSON can carry nulls the non-nullable model can't express; the cached result never holds one.</summary>
    [Fact]
    public async Task GetApplicationSecurityAsync_NullApplicationAccess_BecomesEmpty_AndNullEntriesAreDropped()
    {
        _mockCoreService
            .Setup(s => s.GetApplicationSecurityAsync(ValidToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AsmResponseModel { PositionId = 1, RoleId = 2, ApplicationAccess = null! },
                new AsmResponseModel { PositionId = 3, RoleId = 4, ApplicationAccess = [null!, new ApplicationAccessModel { ModuleCode = "CUST" }] }
            ]);

        var result = await _service.GetApplicationSecurityAsync(ValidToken, Expiry);

        result[0].ApplicationAccess.Should().BeEmpty();
        result[1].ApplicationAccess.Should().ContainSingle().Which.ModuleCode.Should().Be("CUST");
    }

    [Fact]
    public async Task GetApplicationSecurityAsync_EmptyResult_ReturnsEmptyList()
    {
        // Arrange
        string token = ValidToken;
        IReadOnlyList<AsmResponseModel> coreResult = [];

        _mockCoreService
            .Setup(s => s.GetApplicationSecurityAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coreResult);

        // Act
        var result = await _service.GetApplicationSecurityAsync(token, Expiry);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetApplicationSecurityAsync_NullToken_ThrowsArgumentException()
    {
        // Arrange
        const string? token = null;

        // Act
        Func<Task> act = async () => await _service.GetApplicationSecurityAsync(token!, Expiry);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetApplicationSecurityAsync_EmptyToken_ThrowsArgumentException()
    {
        // Arrange
        const string token = "";

        // Act
        Func<Task> act = async () => await _service.GetApplicationSecurityAsync(token, Expiry);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetApplicationSecurityAsync_WhitespaceToken_ThrowsArgumentException()
    {
        // Arrange
        const string token = "   ";

        // Act
        Func<Task> act = async () => await _service.GetApplicationSecurityAsync(token, Expiry);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region Cache Immutability Tests

    /// <summary>Returned permissions are frozen: the L1 entry is shared until the token expires.</summary>
    [Fact]
    public async Task GetApplicationSecurityAsync_ReturnsImmutableCollection()
    {
        // Arrange
        string token = ValidToken;
        IReadOnlyList<AsmResponseModel> coreResult =
        [
            new AsmResponseModel { RoleId = 1, PositionId = 1, ApplicationAccess = [] }
        ];

        _mockCoreService
            .Setup(s => s.GetApplicationSecurityAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coreResult);

        // Act
        var result = await _service.GetApplicationSecurityAsync(token, Expiry);

        // Assert
        IList<AsmResponseDto> asMutable = (IList<AsmResponseDto>)result;
        Action add = () => asMutable.Add(new AsmResponseDto { RoleId = 99, PositionId = 99 });
        Action clear = asMutable.Clear;
        add.Should().Throw<NotSupportedException>();
        clear.Should().Throw<NotSupportedException>();

        // And the cached entry the next request receives is unchanged
        var second = await _service.GetApplicationSecurityAsync(token, Expiry);
        second.Should().HaveCount(1);
        _mockCoreService.Verify(
            s => s.GetApplicationSecurityAsync(token, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ASM Failure Tests

    public static TheoryData<string> Caches => new() { "enabled", "disabled" };

    /// <summary>An ASM failure reaches the caller after one ASM call; it is never retried by the service.</summary>
    [Theory]
    [MemberData(nameof(Caches))]
    public async Task GetApplicationSecurityAsync_AsmFails_CallsAsmOnceAndPropagates(string cache)
    {
        ExternalServiceException outage = new("AsmService", ExternalServiceFailure.Unavailable, "circuit open");
        _mockCoreService
            .Setup(s => s.GetApplicationSecurityAsync(ValidToken, It.IsAny<CancellationToken>()))
            .ThrowsAsync(outage);
        AsmService sut = new(_mockCoreService.Object, cache == "enabled" ? TestCache.Create() : TestCache.CreateDisabled(), TimeProvider.System);

        Func<Task> act = () => sut.GetApplicationSecurityAsync(ValidToken, Expiry);

        (await act.Should().ThrowAsync<ExternalServiceException>()).Which.Should().BeSameAs(outage);
        _mockCoreService.Verify(
            s => s.GetApplicationSecurityAsync(ValidToken, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>A failed lookup is not cached as "no permissions": the next request asks ASM again.</summary>
    [Fact]
    public async Task GetApplicationSecurityAsync_AsmFails_IsNotCached()
    {
        _mockCoreService
            .SetupSequence(s => s.GetApplicationSecurityAsync(ValidToken, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("AsmService", ExternalServiceFailure.Failed, "Succeeded=false"))
            .ReturnsAsync([new AsmResponseModel { PositionId = 1, RoleId = 1 }]);
        AsmService sut = new(_mockCoreService.Object, TestCache.Create(), TimeProvider.System);

        Func<Task> first = () => sut.GetApplicationSecurityAsync(ValidToken, Expiry);
        await first.Should().ThrowAsync<ExternalServiceException>();
        var second = await sut.GetApplicationSecurityAsync(ValidToken, Expiry);

        second.Should().ContainSingle();
        _mockCoreService.Verify(
            s => s.GetApplicationSecurityAsync(ValidToken, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion
}
