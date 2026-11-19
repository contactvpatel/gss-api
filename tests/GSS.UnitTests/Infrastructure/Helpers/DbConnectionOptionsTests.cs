using System.Globalization;
using FluentAssertions;
using GSS.Infrastructure.Extensions;
using GSS.Infrastructure.Helpers;
using GSS.UnitTests.Common;
using GSS.Util.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Helpers;

/// <summary>
/// <c>DbConnectionSettings</c> binds to <see cref="DbConnectionModel"/> and is validated at startup.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DbConnectionOptionsTests
{
    [Fact]
    public void ValidSettings_Bind()
    {
        var model = Resolve(ValidSettings());

        model.Write.Host.Should().Be("db-write");
        model.Read.DatabaseName.Should().Be("shop");
    }

    [Theory]
    [InlineData("Write:Host")]
    [InlineData("Write:DatabaseName")]
    [InlineData("Write:UserId")]
    [InlineData("Read:Host")]
    [InlineData("Read:DatabaseName")]
    [InlineData("Read:UserId")]
    public void MissingRequiredSetting_FailsNamingTheKey(string key)
    {
        var settings = ValidSettings();
        settings["DbConnectionSettings:" + key] = "";

        Action act = () => Resolve(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage($"*DbConnectionSettings:{key}*");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    public void InvalidPort_Fails(string port)
    {
        var settings = ValidSettings();
        settings["DbConnectionSettings:Write:Port"] = port;

        Action act = () => Resolve(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage("*DbConnectionSettings:Write:Port*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Disable")]
    [InlineData("allow")]
    [InlineData("Prefer")]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    [InlineData("VERIFYFULL")]
    public void KnownOrBlankSslMode_Binds(string sslMode)
    {
        var settings = ValidSettings();
        settings["DbConnectionSettings:Write:SslMode"] = sslMode;

        Action act = () => Resolve(settings);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("VerifyFul")]
    [InlineData("Required")]
    [InlineData("1")]
    public void UnknownSslMode_FailsNamingTheKey(string sslMode)
    {
        var settings = ValidSettings();
        settings["DbConnectionSettings:Write:SslMode"] = sslMode;

        Action act = () => Resolve(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage("*DbConnectionSettings:Write:SslMode*");
    }

    [Theory]
    [InlineData("MaxPoolSize", "0")]
    [InlineData("MinPoolSize", "-1")]
    [InlineData("ConnectionIdleLifetime", "9")]
    [InlineData("CommandTimeout", "-1")]
    [InlineData("Timeout", "-1")]
    [InlineData("Timeout", "1025")]
    [InlineData("ConnectionLifetime", "-1")]
    [InlineData("KeepAlive", "-1")]
    [InlineData("MaxAutoPrepare", "-1")]
    [InlineData("AutoPrepareMinUsages", "0")]
    public void OutOfRangeSetting_FailsNamingTheKey(string key, string value)
    {
        var settings = ValidSettings();
        settings["DbConnectionSettings:Read:" + key] = value;

        Action act = () => Resolve(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage($"*DbConnectionSettings:Read:{key}*");
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(null, DatabaseConnectionStrings.DefaultMaxPoolSize + 1)]
    [InlineData(DatabaseConnectionStrings.DefaultMinPoolSize - 1, null)]
    public void MinPoolSizeAboveMaxPoolSize_Fails(int? maxPoolSize, int? minPoolSize)
    {
        var settings = ValidSettings();
        if (maxPoolSize is int max)
        {
            settings["DbConnectionSettings:Write:MaxPoolSize"] = max.ToString(CultureInfo.InvariantCulture);
        }

        if (minPoolSize is int min)
        {
            settings["DbConnectionSettings:Write:MinPoolSize"] = min.ToString(CultureInfo.InvariantCulture);
        }

        Action act = () => Resolve(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage("*DbConnectionSettings:Write:MinPoolSize*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10001")]
    public void OutOfRangeMaxReplicas_FailsNamingTheKey(string value)
    {
        var settings = ValidSettings();
        settings["DbConnectionSettings:MaxReplicas"] = value;

        Action act = () => Resolve(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage("*DbConnectionSettings:MaxReplicas*");
    }

    [Fact]
    public void ReplicaAndPoolerSettings_Bind()
    {
        var settings = ValidSettings();
        settings["DbConnectionSettings:MaxReplicas"] = "6";
        settings["DbConnectionSettings:BehindConnectionPooler"] = "true";

        var model = Resolve(settings);

        model.MaxReplicas.Should().Be(6);
        model.BehindConnectionPooler.Should().BeTrue();
    }

    [Fact]
    public void OldDatabaseConnectionSettingsSection_IsNotRead()
    {
        Dictionary<string, string?> settings = ValidSettings()
            .ToDictionary(kv => kv.Key.Replace("DbConnectionSettings", "DatabaseConnectionSettings"), kv => kv.Value);

        Action act = () => Resolve(settings);

        act.Should().Throw<OptionsValidationException>("one section name: DbConnectionSettings");
    }

    [Fact]
    public async Task InvalidSettings_FailHostStartup()
    {
        var settings = ValidSettings();
        settings["DbConnectionSettings:Write:Host"] = "";
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddInfrastructureDataAccess(builder.Configuration);
        using var host = builder.Build();

        Func<Task> start = () => host.StartAsync();

        await start.Should().ThrowAsync<OptionsValidationException>();
    }

    private static DbConnectionModel Resolve(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddInfrastructureDataAccess(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<DbConnectionModel>>().Value;
    }

    private static Dictionary<string, string?> ValidSettings()
    {
        Dictionary<string, string?> settings = [];
        foreach (string side in new[] { "Read", "Write" })
        {
            string prefix = $"DbConnectionSettings:{side}:";
            settings[prefix + "Host"] = side == "Read" ? "db-read" : "db-write";
            settings[prefix + "Port"] = "5432";
            settings[prefix + "DatabaseName"] = "shop";
            settings[prefix + "UserId"] = "app";
            settings[prefix + "Password"] = "secret";
        }

        return settings;
    }
}
