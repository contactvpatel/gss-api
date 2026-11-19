using FluentAssertions;
using GSS.UnitTests.Common;
using GSS.Util.Validation;
using Xunit;

namespace GSS.UnitTests.Util;

/// <summary>
/// Unit tests for UrlValidator.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class UrlValidatorTests
{
    #region IsValidExternalUrl Tests

    [Fact]
    public void IsValidExternalUrl_ValidHttpsUrl_ReturnsTrue()
    {
        // Arrange
        const string url = "https://api.example.com";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeTrue();
        uri.Should().NotBeNull();
        uri!.Scheme.Should().Be("https");
    }

    [Fact]
    public void IsValidExternalUrl_HttpUrl_ReturnsFalse()
    {
        // Arrange
        const string url = "http://api.example.com";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_Localhost_ReturnsFalse()
    {
        // Arrange
        const string url = "https://localhost";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_127_0_0_1_ReturnsFalse()
    {
        // Arrange
        const string url = "https://127.0.0.1";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_PrivateIpRange_ReturnsFalse()
    {
        // Arrange
        const string url = "https://192.168.1.1";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_10_0_0_1_ReturnsFalse()
    {
        // Arrange
        const string url = "https://10.0.0.1";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_172_16_0_1_ReturnsFalse()
    {
        // Arrange
        const string url = "https://172.16.0.1";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_169_254_0_1_ReturnsFalse()
    {
        // Arrange
        const string url = "https://169.254.0.1";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_InvalidUrl_ReturnsFalse()
    {
        // Arrange
        const string url = "not-a-valid-url";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_NullUrl_ReturnsFalse()
    {
        // Act
        bool result = UrlValidator.IsValidExternalUrl(null, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_EmptyUrl_ReturnsFalse()
    {
        // Act
        bool result = UrlValidator.IsValidExternalUrl(string.Empty, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_ValidPublicIp_ReturnsTrue()
    {
        // Arrange
        const string url = "https://8.8.8.8";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeTrue();
        uri.Should().NotBeNull();
    }

    [Fact]
    public void IsValidExternalUrl_172_31_255_255_PrivateRange_ReturnsFalse()
    {
        // Arrange - 172.31.x.x is in private range (172.16.0.0 - 172.31.255.255)
        const string url = "https://172.31.255.255";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void IsValidExternalUrl_172_15_0_1_OutsidePrivateRange_ReturnsTrue()
    {
        // Arrange - 172.15.x.x is outside 172.16-172.31 range
        const string url = "https://172.15.0.1";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeTrue();
        uri.Should().NotBeNull();
    }

    [Fact]
    public void IsValidExternalUrl_172_32_0_1_OutsidePrivateRange_ReturnsTrue()
    {
        // Arrange - 172.32.x.x is outside 172.16-172.31 range
        const string url = "https://172.32.0.1";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeTrue();
        uri.Should().NotBeNull();
    }

    [Fact]
    public void IsValidExternalUrl_172HostnameNotIp_ReturnsTrue()
    {
        // Arrange - host starting with "172." but not a valid IP (e.g. hostname)
        const string url = "https://172.example.com";

        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert - "172.example.com" does not parse as an IP address, so the private-range rules
        // never apply and the host is treated as an ordinary DNS name
        result.Should().BeTrue();
        uri.Should().NotBeNull();
    }

    [Theory]
    // Loopback host names resolved by the fast path rather than by address parsing
    [InlineData("https://localhost.localdomain")]
    [InlineData("https://ip6-localhost")]
    [InlineData("https://ip6-loopback")]
    [InlineData("https://IP6-Loopback")]
    // Rest of 127.0.0.0/8 - the old prefix blocklist only matched the exact string "127.0.0.1"
    [InlineData("https://127.0.0.2")]
    [InlineData("https://127.255.255.254")]
    // Alternate IPv4 encodings of 127.0.0.1 (short form, decimal, hex, octal)
    [InlineData("https://127.1")]
    [InlineData("https://2130706433")]
    [InlineData("https://0x7f000001")]
    [InlineData("https://0177.0.0.1")]
    // IPv4-mapped IPv6
    [InlineData("https://[::ffff:127.0.0.1]")]
    [InlineData("https://[::ffff:10.0.0.1]")]
    [InlineData("https://[::ffff:169.254.1.1]")]
    // IPv6 loopback and unspecified
    [InlineData("https://[::1]")]
    [InlineData("https://[::]")]
    // IPv6 unique-local fc00::/7
    [InlineData("https://[fc00::1]")]
    [InlineData("https://[fd00::1]")]
    [InlineData("https://[fdff:ffff::1]")]
    // IPv6 link-local fe80::/10
    [InlineData("https://[fe80::1]")]
    [InlineData("https://[febf:ffff::1]")]
    public void IsValidExternalUrl_LoopbackOrPrivateEncoding_ReturnsFalse(string url)
    {
        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Theory]
    // Positive controls - genuinely public addresses must still validate
    [InlineData("https://api.example.com/v1/")]
    [InlineData("https://8.8.8.8")]
    [InlineData("https://1.1.1.1")]
    [InlineData("https://[2606:4700:4700::1111]")]
    // fec0::/10 site-local is deprecated and outside fe80::/10, so it is not blocked
    [InlineData("https://[fec0::1]")]
    public void IsValidExternalUrl_PublicAddress_ReturnsTrue(string url)
    {
        // Act
        bool result = UrlValidator.IsValidExternalUrl(url, out var uri);

        // Assert
        result.Should().BeTrue();
        uri.Should().NotBeNull();
    }

    [Fact]
    public void IsValidExternalUrl_WhitespaceUrl_ReturnsFalse()
    {
        // Act
        bool result = UrlValidator.IsValidExternalUrl("   ", out var uri);

        // Assert
        result.Should().BeFalse();
        uri.Should().BeNull();
    }

    #endregion
}
