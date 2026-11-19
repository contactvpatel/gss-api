using FluentAssertions;
using GSS.Api.Extensions.Core;
using GSS.UnitTests.Common;
using GSS.Util.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GSS.UnitTests.API.Extensions;

/// <summary>
/// SSO/MIS/ASM settings are validated at startup: each URL must be HTTPS to an external host, nested <c>Endpoint</c>
/// members run their attributes, and each path must be relative to the service URL.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ServiceOptionsValidationTests
{
    /// <summary>Used to be checked only when the client was first built: every request 500ed while readiness passed.</summary>
    [Theory]
    [InlineData("SsoService", "http://sso.example.com/api/")]
    [InlineData("SsoService", "https://localhost/api/")]
    [InlineData("SsoService", "https://10.0.0.5/api/")]
    [InlineData("SsoService", "https://169.254.169.254/latest/")]
    [InlineData("SsoService", "not a url")]
    [InlineData("MisService", "http://mis.example.com/api/v1/")]
    [InlineData("MisService", "https://192.168.1.10/api/v1/")]
    [InlineData("AsmService", "http://asm.example.com/api/v2/")]
    [InlineData("AsmService", "https://[::1]/api/v2/")]
    public void Url_NotExternalHttps_FailsNamingTheKey(string service, string url)
    {
        var settings = ValidSettings();
        settings[$"{service}:Url"] = url;

        Action act = service switch
        {
            "SsoService" => () => Resolve<SsoServiceOptions>(settings),
            "MisService" => () => Resolve<MisServiceOptions>(settings),
            _ => () => Resolve<AsmServiceOptions>(settings)
        };

        act.Should().Throw<OptionsValidationException>().WithMessage("*Url*HTTPS URL to an external host*");
    }

    [Fact]
    public void ValidSettings_Bind()
    {
        Resolve<SsoServiceOptions>(ValidSettings()).Endpoint.ValidateToken.Should().Be("user/validate/token");
        Resolve<MisServiceOptions>(ValidSettings()).Endpoint.Department.Should().Be("Department");
        Resolve<AsmServiceOptions>(ValidSettings()).Endpoint.ApplicationSecurity.Should().Be("application-security");
    }

    [Theory]
    [InlineData("/user/validate/token")]
    [InlineData("\\user\\validate\\token")]
    [InlineData("https://elsewhere.example.com/collect")]
    [InlineData("//elsewhere.example.com/collect")]
    public void SsoEndpoint_NotRelative_FailsNamingTheKey(string path)
    {
        var settings = ValidSettings();
        settings["SsoService:Endpoint:ValidateToken"] = path;

        Action act = () => Resolve<SsoServiceOptions>(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage("*Endpoint.ValidateToken*relative*");
    }

    [Fact]
    public void MisEndpoint_LeadingSlash_Fails()
    {
        var settings = ValidSettings();
        settings["MisService:Endpoint:Role"] = "/Role";

        Action act = () => Resolve<MisServiceOptions>(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage("*Endpoint.Role*");
    }

    [Fact]
    public void AsmEndpoint_LeadingSlash_Fails()
    {
        var settings = ValidSettings();
        settings["AsmService:Endpoint:ApplicationSecurity"] = "/application-security";

        Action act = () => Resolve<AsmServiceOptions>(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage("*Endpoint.ApplicationSecurity*");
    }

    /// <summary>[Required] on nested endpoint members used to be skipped by ValidateDataAnnotations.</summary>
    [Fact]
    public void SsoEndpoint_Missing_Fails()
    {
        var settings = ValidSettings();
        settings.Remove("SsoService:Endpoint:Logout");

        Action act = () => Resolve<SsoServiceOptions>(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage("*Endpoint.Logout*");
    }

    /// <summary>
    /// A missing or blank app credential used to pass startup: every MIS/ASM call then went out without it, got 401 and
    /// answered 502. The message names the key, never a value.
    /// </summary>
    [Theory]
    [InlineData("MisService", "AuthAppId", null)]
    [InlineData("MisService", "AuthAppSecret", "")]
    [InlineData("MisService", "AuthAppSecret", "   ")]
    [InlineData("AsmService", "AuthAppId", "")]
    [InlineData("AsmService", "AuthAppSecret", null)]
    public void AppCredential_MissingOrBlank_FailsNamingTheKey(string service, string key, string? value)
    {
        var settings = ValidSettings();
        if (value is null)
        {
            settings.Remove($"{service}:Headers:{key}");
        }
        else
        {
            settings[$"{service}:Headers:{key}"] = value;
        }

        Action act = service == "MisService" ? () => Resolve<MisServiceOptions>(settings) : () => Resolve<AsmServiceOptions>(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage($"*Headers.{key}*")
            .Which.Message.Should().NotContain("secret");
    }

    private static T Resolve<T>(Dictionary<string, string?> settings)
        where T : class
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.ConfigureApplicationSettings(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<T>>().Value;
    }

    private static Dictionary<string, string?> ValidSettings()
    {
        return new Dictionary<string, string?>
        {
            ["SsoService:Url"] = "https://sso.example.com/api/",
            ["SsoService:Endpoint:ValidateToken"] = "user/validate/token",
            ["SsoService:Endpoint:RenewToken"] = "user/renewtoken",
            ["SsoService:Endpoint:Logout"] = "user/logout",
            ["MisService:Url"] = "https://mis.example.com/api/v1/",
            ["MisService:Headers:AuthAppId"] = "mis-app",
            ["MisService:Headers:AuthAppSecret"] = "mis-secret",
            ["MisService:Endpoint:GeoLevel"] = "Division/GeoLevel",
            ["MisService:Endpoint:Department"] = "Department",
            ["MisService:Endpoint:Entity"] = "Entity",
            ["MisService:Endpoint:RoleType"] = "Division/RoleType",
            ["MisService:Endpoint:Role"] = "Role",
            ["MisService:Endpoint:Position"] = "Position",
            ["MisService:Endpoint:PersonPosition"] = "Person/Position",
            ["AsmService:Url"] = "https://asm.example.com/api/v2/",
            ["AsmService:Headers:AuthAppId"] = "asm-app",
            ["AsmService:Headers:AuthAppSecret"] = "asm-secret",
            ["AsmService:Endpoint:ApplicationSecurity"] = "application-security"
        };
    }
}
