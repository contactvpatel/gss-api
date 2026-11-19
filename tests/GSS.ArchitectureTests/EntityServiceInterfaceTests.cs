using FluentAssertions;
using GSS.Business.Services.Base;
using GSS.Business.Services.Interfaces;
using Xunit;

namespace GSS.ArchitectureTests;

[Trait("Category", TestCategories.Unit)]
public class EntityServiceInterfaceTests
{
    [Fact]
    public void EntityServiceBase_ImplementsTheSharedCrudContract()
    {
        Type[] interfaces = typeof(EntityServiceBase<,,,>).GetInterfaces();

        interfaces.Should().ContainSingle(type =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEntityService<,,>));
    }
}
