using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using GSS.Api.Authorization;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.API.Authorization;

[Trait("Category", TestCategories.Unit)]
public class ModuleCodeExtensionsTests
{
    [Fact]
    public void ToAsmCode_IsTheDescriptionOfEveryModule()
    {
        foreach (var module in Enum.GetValues<ModuleCode>())
        {
            string? description = typeof(ModuleCode).GetField(module.ToString())!.GetCustomAttribute<DescriptionAttribute>()?.Description;

            description.Should().NotBeNullOrWhiteSpace($"ModuleCode.{module} needs its ASM code in [Description]");
            module.ToAsmCode().Should().Be(description);
        }
    }

    [Fact]
    public void ToAsmCode_IsUniquePerModule()
    {
        Enum.GetValues<ModuleCode>().Select(m => m.ToAsmCode()).Should().OnlyHaveUniqueItems();
    }

}
