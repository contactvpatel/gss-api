using FluentAssertions;
using GSS.Infrastructure.Helpers;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>
/// Unit tests for HttpClientExtensions.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class HttpClientExtensionsTests
{
    #region AsJsonContent Tests

    [Fact]
    public async Task AsJsonContent_IsCamelCaseUtf8Json_WithAContentLength()
    {
        using var content = new GSS.Core.Models.RenewTokenRequest { RefreshToken = "t" }.AsJsonContent();

        long? length = content.Headers.ContentLength;
        string body = await content.ReadAsStringAsync();
        body.Should().Be(@"{""refreshToken"":""t""}");
        length.Should().Be(body.Length);
        content.Headers.ContentType!.MediaType.Should().Be("application/json");
        content.Headers.ContentType.CharSet.Should().Be("utf-8");
    }

    #endregion

    #region HttpRequestMessage SetBearerToken Tests

    [Fact]
    public void SetBearerToken_HttpRequestMessage_ValidToken_SetsAuthorizationHeader()
    {
        // Arrange
        HttpRequestMessage request = new(HttpMethod.Get, "https://example.com");
        const string token = "test-token";

        // Act
        var result = request.SetBearerToken(token);

        // Assert
        result.Should().BeSameAs(request);
        request.Headers.GetValues("Authorization").Should().Equal(token);
    }

    #endregion

    #region URL Helper Tests

    [Fact]
    public void EnsureTrailingSlash_UrlWithoutSlash_AddsSlash()
    {
        // Arrange
        const string url = "https://example.com/api";

        // Act
        string result = url.EnsureTrailingSlash();

        // Assert
        result.Should().Be("https://example.com/api/");
    }

    [Fact]
    public void EnsureTrailingSlash_UrlWithSlash_KeepsSlash()
    {
        // Arrange
        const string url = "https://example.com/api/";

        // Act
        string result = url.EnsureTrailingSlash();

        // Assert
        result.Should().Be("https://example.com/api/");
    }

    [Fact]
    public void EnsureTrailingSlash_NullOrEmpty_ReturnsAsIs()
    {
        // Act & Assert
        ((string?)null!).EnsureTrailingSlash().Should().BeNull();
        string.Empty.EnsureTrailingSlash().Should().Be(string.Empty);
    }

    #endregion
}
