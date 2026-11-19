using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using GSS.Api.Serialization;
using GSS.UnitTests.Common;
using GSS.Util.Time;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace GSS.UnitTests.API.Serialization;

[Trait("Category", TestCategories.Unit)]
public class UtcDateTimeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new UtcDateTimeJsonConverter() } };

    [Fact]
    public void Normalize_Unspecified_IsTreatedAsUtc()
    {
        var result = UtcDateTime.Normalize(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Unspecified));

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    }

    [Fact]
    public void Normalize_Local_IsConvertedToUtc()
    {
        var local = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc).ToLocalTime();

        UtcDateTime.Normalize(local).Should().Be(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("\"2025-01-02T03:04:05\"", "2025-01-02T03:04:05")]
    [InlineData("\"2025-01-02T03:04:05Z\"", "2025-01-02T03:04:05")]
    [InlineData("\"2025-01-02T08:34:05+05:30\"", "2025-01-02T03:04:05")]
    public void JsonRead_AlwaysReturnsUtc(string json, string expectedUtc)
    {
        var result = JsonSerializer.Deserialize<DateTime>(json, JsonOptions);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(DateTime.SpecifyKind(DateTime.Parse(expectedUtc, CultureInfo.InvariantCulture), DateTimeKind.Utc));
    }

    [Fact]
    public void JsonWrite_UnspecifiedAndNullable_WriteZSuffix()
    {
        DateTime? value = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

        JsonSerializer.Serialize(value, JsonOptions).Should().Be("\"2025-01-02T03:04:05Z\"");
    }

    [Theory]
    [InlineData("2025-01-02", "2025-01-02T00:00:00")]
    [InlineData("2025-01-02T03:04:05Z", "2025-01-02T03:04:05")]
    [InlineData("2025-01-02T08:34:05+05:30", "2025-01-02T03:04:05")]
    public async Task ModelBinder_BindsAsUtc(string input, string expectedUtc)
    {
        var context = CreateBindingContext(input);

        await new UtcDateTimeModelBinder().BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        DateTime bound = (DateTime)context.Result.Model!;
        bound.Kind.Should().Be(DateTimeKind.Utc);
        bound.Should().Be(DateTime.SpecifyKind(DateTime.Parse(expectedUtc, CultureInfo.InvariantCulture), DateTimeKind.Utc));
    }

    [Fact]
    public async Task ModelBinder_InvalidValue_AddsModelError()
    {
        var context = CreateBindingContext("not-a-date");

        await new UtcDateTimeModelBinder().BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.ErrorCount.Should().Be(1);
    }

    private static DefaultModelBindingContext CreateBindingContext(string value)
    {
        Mock<IValueProvider> valueProvider = new();
        valueProvider.Setup(v => v.GetValue("date")).Returns(new ValueProviderResult(new StringValues(value)));
        return new DefaultModelBindingContext
        {
            ModelName = "date",
            ModelState = new ModelStateDictionary(),
            ValueProvider = valueProvider.Object
        };
    }
}
