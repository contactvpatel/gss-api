using FluentAssertions;
using GSS.Infrastructure.Helpers;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>
/// Unit tests for SensitiveDataSanitizer.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class SensitiveDataSanitizerTests
{
    #region Sanitize Tests

    [Fact]
    public void Sanitize_NullInput_ReturnsEmptyString()
    {
        // Act
        string result = SensitiveDataSanitizer.Sanitize(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_EmptyInput_ReturnsEmptyString()
    {
        // Act
        string result = SensitiveDataSanitizer.Sanitize(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_WhitespaceInput_ReturnsWhitespace()
    {
        // Arrange
        const string input = "   ";

        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        result.Should().Be(input);
    }

    [Fact]
    public void Sanitize_JwtToken_MasksToken()
    {
        // Arrange
        const string input = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        result.Should().Be("Bearer ***");
    }

    [Fact]
    public void Sanitize_UnsignedJwt_MasksWholeToken()
    {
        // SSO v1 tokens have an empty signature, so the tail is claims.
        string result = SensitiveDataSanitizer.Sanitize("token eyJhbGciOiJub25lIn0.eyJwaWQiOiIxMjMifQ. rejected");

        result.Should().Be("token *** rejected");
    }

    [Theory]
    // An upstream's error id and status must stay matchable with its own logs.
    [InlineData("Succeeded=false (3f2504e0-4f89-11d3-9a0c-0305e82c3301-500-Internal error)")]
    [InlineData("Failed at gss-mis.example.org/api/internationalization")]
    [InlineData("traceparent 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")]
    public void Sanitize_IdsHostsAndLongWords_AreKept(string input)
    {
        SensitiveDataSanitizer.Sanitize(input).Should().Be(input);
    }

    [Fact]
    public void Sanitize_Password_MasksPassword()
    {
        // Arrange
        const string input = "password: secret123";

        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        result.Should().Contain("password: ***");
        result.Should().NotContain("secret123");
    }

    [Fact]
    public void Sanitize_CreditCard_MasksCreditCard()
    {
        // Arrange
        const string input = "Card number: 1234-5678-9012-3456";

        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        result.Should().Contain("****-****-****-****");
        result.Should().NotContain("1234-5678-9012-3456");
    }

    [Fact]
    public void Sanitize_Ssn_MasksSsn()
    {
        // Arrange
        const string input = "SSN: 123-45-6789";

        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        result.Should().Contain("***-**-****");
        result.Should().NotContain("123-45-6789");
    }

    [Theory]
    [InlineData("{\"password\":\"hunter2\"}", "hunter2")]
    [InlineData("{\"apiSecret\": \"abc123\"}", "abc123")]
    [InlineData("Password=Sup3rSecret!;", "Sup3rSecret!")]
    [InlineData("password: hunter2", "hunter2")]
    // Prefixed keys: snake_case, camelCase and kebab-case.
    [InlineData("{\"access_token\":\"short-abc123\"}", "short-abc123")]
    [InlineData("{\"refresh_token\":\"r-9876\"}", "r-9876")]
    [InlineData("{\"id_token\":\"i-555\"}", "i-555")]
    [InlineData("{\"client_secret\":\"s3cr3t\"}", "s3cr3t")]
    [InlineData("{\"clientSecret\":\"s3cr3t\"}", "s3cr3t")]
    [InlineData("{\"refreshToken\":\"r-9876\"}", "r-9876")]
    [InlineData("{\"api_key\":\"k-123\"}", "k-123")]
    [InlineData("x-api-key: k-123", "k-123")]
    public void Sanitize_SecretInAnySupportedEncoding_RedactsValue(string input, string secret)
    {
        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        result.Should().NotContain(secret);
        result.Should().Contain("***");
    }

    [Fact]
    public void Sanitize_FormEncodedSecrets_RedactsEachValue()
    {
        string result = SensitiveDataSanitizer.Sanitize("grant_type=refresh&refresh_token=abc123&client_secret=xyz");

        result.Should().NotContain("abc123").And.NotContain("xyz");
        result.Should().Contain("grant_type=refresh");
    }

    [Fact]
    public void Sanitize_NestedJsonSecret_RedactsValueAndKeepsOtherFields()
    {
        // Arrange
        const string input = "{\"error\":{\"detail\":{\"password\":\"hunter2\",\"token\":\"abc123\"}},\"user\":\"bob\"}";

        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        result.Should().NotContain("hunter2");
        result.Should().NotContain("abc123");
        result.Should().Contain("bob");
    }

    [Theory]
    // Words that merely contain "pass" / "secret" must not be treated as secret keys
    [InlineData("{\"compass\":\"north\"}", "north")]
    [InlineData("{\"bypass\":\"yes\"}", "yes")]
    [InlineData("Compass=north;", "north")]
    public void Sanitize_KeyContainingSecretWordAsSubstring_LeavesValueIntact(string input, string value)
    {
        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        result.Should().Contain(value);
        result.Should().NotContain("***");
    }

    [Fact]
    public void Sanitize_QuotedJsonKey_KeepsKeyVisible()
    {
        // Arrange
        const string input = "{\"password\":\"hunter2\"}";

        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert - the key must survive so the log line stays diagnosable
        result.Should().Contain("password");
    }

    [Fact]
    public void Sanitize_NoSensitiveData_ReturnsOriginal()
    {
        // Arrange
        const string input = "This is a normal message without sensitive data";

        // Act
        string result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        result.Should().Be(input);
    }

    #endregion
}
