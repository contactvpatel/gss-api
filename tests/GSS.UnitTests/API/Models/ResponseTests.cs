using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using GSS.Api.Models;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.API.Models;

/// <summary>The envelope is built only through <see cref="Response{T}.Success"/> and <see cref="Response{T}.Failure"/>.</summary>
[Trait("Category", TestCategories.Unit)]
public class ResponseTests
{
    [Fact]
    public void Envelope_HasNoPublicConstructorsOrMutableProperties()
    {
        typeof(Response<int>).GetConstructors().Should().BeEmpty(because: "use Success or Failure");
        typeof(Response<int>).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter))
            .Select(p => p.Name)
            .Should().BeEmpty(because: "an envelope is not changed after it is built");
    }

    [Fact]
    public void Success_HasDataAndNoErrors()
    {
        Response<int> response = Response<int>.Success(42, "ok");

        response.Succeeded.Should().BeTrue();
        response.Data.Should().Be(42);
        response.Message.Should().Be("ok");
        response.Errors.Should().BeNull();
    }

    [Fact]
    public void Failure_HasErrorsAndNoData()
    {
        Response<int> response = Response<int>.Failure("bad", [new ApiError { ErrorId = "e1", StatusCode = 400, Message = "x" }]);

        response.Succeeded.Should().BeFalse();
        response.Data.Should().Be(0);
        response.Errors.Should().ContainSingle().Which.ErrorId.Should().Be("e1");
        Response<int>.Failure("bad").Errors.Should().BeEmpty();
    }

    [Fact]
    public void Envelope_RoundTripsThroughJson()
    {
        string json = JsonSerializer.Serialize(Response<string>.Success("data", "ok"), JsonSerializerOptions.Web);

        var read = JsonSerializer.Deserialize<Response<string>>(json, JsonSerializerOptions.Web);

        read.Should().BeEquivalentTo(Response<string>.Success("data", "ok"));
    }

    private static bool IsInitOnly(MethodInfo setter)
    {
        return setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }
}
