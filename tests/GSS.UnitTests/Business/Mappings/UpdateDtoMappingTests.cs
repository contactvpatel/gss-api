using FluentAssertions;
using GSS.UnitTests.Common;
using GSS.UnitTests.Common.Models;
using Mapster;
using Xunit;

namespace GSS.UnitTests.Business.Mappings;

[Trait("Category", TestCategories.Unit)]
public class UpdateDtoMappingTests
{
    [Fact]
    public void UpdateDtoField_ReachesTheEntity()
    {
        TestEntity entity = new() { Name = "before" };

        new UpdateTestEntityDto { Name = "after" }.Adapt(entity);

        entity.Name.Should().Be("after");
    }

    [Fact]
    public void NullUpdateDtoField_KeepsTheCurrentValue()
    {
        TestEntity entity = new() { Name = "before" };

        new UpdateTestEntityDto().Adapt(entity);

        entity.Name.Should().Be("before");
    }
}
