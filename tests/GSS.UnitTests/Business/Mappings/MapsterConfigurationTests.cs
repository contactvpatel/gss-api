using FluentAssertions;
using GSS.Business.DTOs;
using GSS.Business.Mappings;
using GSS.Core.Models;
using GSS.UnitTests.Common;
using Mapster;
using Xunit;

namespace GSS.UnitTests.Business.Mappings;

/// <summary>
/// The Mapster configuration compiles under its strict settings (every type pair registered, every destination member
/// sourced or ignored) and the mappings the services use behave as expected. Uses its own config, not GlobalSettings.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class MapsterConfigurationTests
{
    private readonly TypeAdapterConfig _config = MapsterConfiguration.Configure(new TypeAdapterConfig());

    [Fact]
    public void Configure_Compiles()
    {
        // A renamed or missing property fails here (and at startup) instead of mapping to default.
        Action act = () => _config.Compile(failFast: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void Configure_UnregisteredPair_Throws()
    {
        Action act = () => new DepartmentModel().Adapt<RoleDto>(_config);

        act.Should().Throw<Exception>(because: "RequireExplicitMapping rejects pairs without a configuration");
    }

    [Fact]
    public void ExternalModels_MapAsCollections_IncludingNestedAccess()
    {
        List<DepartmentModel> departments = [new() { DepartmentId = 5, DepartmentName = "Finance", DivisionId = 2 }];
        List<AsmResponseModel> security =
        [
            new() { RoleId = 1, PositionId = 9, ApplicationAccess = [new() { ModuleCode = "APPLICATION", HasViewAccess = true }] }
        ];

        var departmentDtos = departments.Adapt<IReadOnlyList<DepartmentDto>>(_config);
        var securityDtos = security.Adapt<List<AsmResponseDto>>(_config);

        departmentDtos.Should().ContainSingle().Which.Should().BeEquivalentTo(new { DepartmentId = 5, DepartmentName = "Finance", DivisionId = 2 });
        securityDtos.Should().ContainSingle().Which.ApplicationAccess.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { ModuleCode = "APPLICATION", HasViewAccess = true });
    }
}
