using FluentAssertions;
using GSS.Business.DTOs;
using GSS.Business.Services;
using GSS.Core.Models;
using GSS.UnitTests.Common;
using Moq;
using Xunit;

namespace GSS.UnitTests.Business.Services;

/// <summary>
/// Unit tests for MisService: mapping, ordering and caching.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class MisServiceTests
{
    private readonly Mock<Core.Interfaces.Services.IMisService> _mockCoreService = new();
    private static readonly DateTimeOffset TokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1);

    private readonly MisService _service;

    public MisServiceTests()
    {
        _service = new MisService(_mockCoreService.Object, TestCache.Create(), TimeProvider.System);

        // Person positions enrich from the department list; default to none
        _mockCoreService
            .Setup(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    #region Role By Id

    [Fact]
    public async Task GetRoleByIdAsync_RoleInDivision3_ReturnsIt()
    {
        _mockCoreService
            .Setup(s => s.GetRoleByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleModel { Id = 42, DivisionId = 3, Name = "Role 42" });

        var result = await _service.GetRoleByIdAsync(42);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.DivisionId.Should().Be(3);
        _mockCoreService.Verify(m => m.GetAllRolesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRoleByIdAsync_CalledTwice_CallsMisOnce()
    {
        _mockCoreService
            .Setup(s => s.GetRoleByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleModel { Id = 42, DivisionId = 3, Name = "Role 42" });

        await _service.GetRoleByIdAsync(42);
        await _service.GetRoleByIdAsync(42);

        _mockCoreService.Verify(m => m.GetRoleByIdAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRoleByIdAsync_Missing_ReturnsNullAndIsCachedBriefly()
    {
        _mockCoreService
            .Setup(s => s.GetRoleByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleModel?)null);

        var first = await _service.GetRoleByIdAsync(42);
        var second = await _service.GetRoleByIdAsync(42);

        first.Should().BeNull();
        second.Should().BeNull();
        _mockCoreService.Verify(m => m.GetRoleByIdAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Basic Functionality Tests

    [Fact]
    public async Task GetAllDepartmentsAsync_ValidDivisionId_ReturnsOrderedDepartments()
    {
        // Arrange - core returns unordered models; service maps and orders by name
        const int divisionId = 1;
        List<DepartmentModel> models =
        [
            new DepartmentModel { DepartmentId = 2, DepartmentName = "Beta Department", DivisionId = divisionId },
            new DepartmentModel { DepartmentId = 1, DepartmentName = "Alpha Department", DivisionId = divisionId }
        ];

        _mockCoreService
            .Setup(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _service.GetAllDepartmentsAsync(divisionId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].DepartmentName.Should().Be("Alpha Department");
        result[1].DepartmentName.Should().Be("Beta Department");
    }

    [Fact]
    public async Task GetAllDepartmentsAsync_SameDivision_SecondCallServedFromCache()
    {
        // Arrange
        const int divisionId = 1;
        List<DepartmentModel> models =
        [
            new DepartmentModel { DepartmentId = 1, DepartmentName = "Alpha Department", DivisionId = divisionId }
        ];

        _mockCoreService
            .Setup(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        await _service.GetAllDepartmentsAsync(divisionId);
        var result = await _service.GetAllDepartmentsAsync(divisionId);

        // Assert
        result.Should().HaveCount(1);
        _mockCoreService.Verify(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllDepartmentsAsync_EmptyResult_ReturnsEmptyList()
    {
        // Arrange
        const int divisionId = 1;

        _mockCoreService
            .Setup(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.GetAllDepartmentsAsync(divisionId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllDepartmentsAsync_CoreServiceThrowsException_PropagatesException()
    {
        // Arrange - cache miss runs the factory; core service throws
        const int divisionId = 1;

        _mockCoreService
            .Setup(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("External service error"));

        // Act & Assert
        Func<Task> act = async () => await _service.GetAllDepartmentsAsync(divisionId);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetAllRolesAsync_ValidDivisionId_ReturnsOrderedRoles()
    {
        // Arrange
        const int divisionId = 1;
        List<RoleModel> models =
        [
            new RoleModel { Id = 2, Name = "Beta Role", DepartmentId = 1, RoleTypeId = 1, DivisionId = divisionId },
            new RoleModel { Id = 1, Name = "Alpha Role", DepartmentId = 1, RoleTypeId = 1, DivisionId = divisionId }
        ];

        _mockCoreService
            .Setup(s => s.GetAllRolesAsync(divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _service.GetAllRolesAsync(divisionId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Alpha Role");
        result[1].Name.Should().Be("Beta Role");
    }

    [Fact]
    public async Task GetAllRolesAsync_DifferentDivisions_CachedSeparately()
    {
        // Arrange - each division has its own roles upstream
        _mockCoreService
            .Setup(s => s.GetAllRolesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RoleModel { Id = 1, Name = "Division 1 Role", DepartmentId = 1, RoleTypeId = 1, DivisionId = 1 }]);
        _mockCoreService
            .Setup(s => s.GetAllRolesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RoleModel { Id = 2, Name = "Division 2 Role", DepartmentId = 2, RoleTypeId = 1, DivisionId = 2 }]);

        // Act
        await _service.GetAllRolesAsync(1);
        var division2 = await _service.GetAllRolesAsync(2);

        // Assert - division 2 must not be served division 1's cached roles
        division2.Should().ContainSingle().Which.Name.Should().Be("Division 2 Role");
        _mockCoreService.Verify(s => s.GetAllRolesAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllRoleTypesAsync_DifferentDivisions_CachedSeparately()
    {
        // Arrange - each division has its own role types upstream
        _mockCoreService
            .Setup(s => s.GetAllRoleTypesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RoleTypeModel { Id = 1, Name = "Division 1 Role Type", DivisionId = 1 }]);
        _mockCoreService
            .Setup(s => s.GetAllRoleTypesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RoleTypeModel { Id = 2, Name = "Division 2 Role Type", DivisionId = 2 }]);

        // Act
        await _service.GetAllRoleTypesAsync(1);
        var division2 = await _service.GetAllRoleTypesAsync(2);

        // Assert - division 2 must not be served division 1's cached role types
        division2.Should().ContainSingle().Which.Name.Should().Be("Division 2 Role Type");
        _mockCoreService.Verify(s => s.GetAllRoleTypesAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllRoleTypesAsync_ValidDivisionId_ReturnsOrderedRoleTypes()
    {
        // Arrange
        const int divisionId = 1;
        List<RoleTypeModel> models =
        [
            new RoleTypeModel { Id = 2, Name = "Beta Role Type", DivisionId = divisionId },
            new RoleTypeModel { Id = 1, Name = "Alpha Role Type", DivisionId = divisionId }
        ];

        _mockCoreService
            .Setup(s => s.GetAllRoleTypesAsync(divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _service.GetAllRoleTypesAsync(divisionId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Alpha Role Type");
        result[1].Name.Should().Be("Beta Role Type");
    }

    [Fact]
    public async Task GetRolesByDepartmentIdAsync_ValidDepartmentId_ReturnsOrderedRoles()
    {
        // Arrange
        const int departmentId = 1;
        List<RoleModel> models =
        [
            new RoleModel { Id = 2, Name = "Beta Role", DepartmentId = departmentId, RoleTypeId = 1, DivisionId = 1 },
            new RoleModel { Id = 1, Name = "Alpha Role", DepartmentId = departmentId, RoleTypeId = 1, DivisionId = 1 }
        ];

        _mockCoreService
            .Setup(s => s.GetRolesByDepartmentIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _service.GetRolesByDepartmentIdAsync(departmentId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Alpha Role");
        result[1].Name.Should().Be("Beta Role");
    }

    [Fact]
    public async Task GetPositionsByRoleIdAsync_ValidRoleId_ReturnsPositions()
    {
        // Arrange
        const int roleId = 1;
        List<PositionModel> models = new List<PositionModel>
        {
            new() { Id = 1, Name = "Position 1", RoleId = roleId },
            new() { Id = 2, Name = "Position 2", RoleId = roleId }
        };

        _mockCoreService
            .Setup(s => s.GetPositionsByRoleIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _service.GetPositionsByRoleIdAsync(roleId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[1].Id.Should().Be(2);
        _mockCoreService.Verify(s => s.GetPositionsByRoleIdAsync(roleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPersonPositionsAsync_EnrichesDepartmentsAndOrdersByEntityName()
    {
        // Arrange - MIS position "dept" entries carry only partial data
        const string personId = "123";
        Guid departmentUuid = Guid.NewGuid();
        List<PersonPositionModel> models =
        [
            new() { PersonId = 123, PositionId = 2, RoleId = 2, EntityName = "Beta", Department = [new() { DepartmentId = 1 }] },
            new() { PersonId = 123, PositionId = 1, RoleId = 1, EntityName = "Alpha", Department = [new() { DepartmentId = 7, DepartmentName = "Dept 7" }] },
            new() { PersonId = 123, PositionId = 3, RoleId = 3, EntityName = "Gamma", Department = null }
        ];
        _mockCoreService
            .Setup(s => s.GetPersonPositionsAsync(personId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);
        _mockCoreService
            .Setup(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DepartmentModel { DepartmentId = 7, DepartmentUuid = departmentUuid, DivisionId = 3, IsAdministrationDepartment = true }]);

        // Act
        var result = await _service.GetPersonPositionsAsync(personId, TokenExpiresAtUtc);

        // Assert
        result.Select(p => p.EntityName).Should().Equal("Alpha", "Beta", "Gamma");
        var enriched = result[0].Department.Should().ContainSingle().Subject;
        enriched.DivisionId.Should().Be(3);
        enriched.DepartmentUuid.Should().Be(departmentUuid);
        enriched.IsAdministrationDepartment.Should().BeTrue();
        enriched.DepartmentName.Should().Be("Dept 7", because: "only division, UUID and flags are enriched");
        result[1].Department.Should().ContainSingle().Which.DivisionId.Should().Be(0, because: "unknown departments stay as MIS sent them");
        result[2].Department.Should().BeEmpty(because: "MIS may omit dept");
    }

    [Fact]
    public async Task GetPersonPositionsAsync_WithTokenLifetime_IsCachedForSignIn()
    {
        _mockCoreService.Setup(s => s.GetPersonPositionsAsync("123", It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await _service.GetPersonPositionsAsync("123", TokenExpiresAtUtc);
        await _service.GetPersonPositionsAsync("123", TokenExpiresAtUtc);

        _mockCoreService.Verify(s => s.GetPersonPositionsAsync("123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPersonPositionsAsync_WithoutTokenLifetime_IsNotCached()
    {
        _mockCoreService.Setup(s => s.GetPersonPositionsAsync("123", It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await _service.GetPersonPositionsAsync("123", null);
        await _service.GetPersonPositionsAsync("123", null);

        _mockCoreService.Verify(s => s.GetPersonPositionsAsync("123", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetPersonPositionsAsync_TokenAlreadyExpiredByClock_IsNotCached()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        MisService lateService = new(_mockCoreService.Object, TestCache.Create(), new FixedTimeProvider(expiresAt.AddMinutes(1)));
        _mockCoreService.Setup(s => s.GetPersonPositionsAsync("123", It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await lateService.GetPersonPositionsAsync("123", expiresAt);
        await lateService.GetPersonPositionsAsync("123", expiresAt);

        _mockCoreService.Verify(s => s.GetPersonPositionsAsync("123", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public async Task GetPersonPositionsAsync_NullPersonId_ThrowsArgumentException()
    {
        // Arrange
        const string? personId = null;

        // Act & Assert
        Func<Task> act = async () => await _service.GetPersonPositionsAsync(personId!, TokenExpiresAtUtc);
        await act.Should().ThrowAsync<ArgumentException>();
        _mockCoreService.Verify(s => s.GetPersonPositionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPersonPositionsAsync_EmptyPersonId_ThrowsArgumentException()
    {
        // Arrange
        const string personId = "";

        // Act & Assert
        Func<Task> act = async () => await _service.GetPersonPositionsAsync(personId, TokenExpiresAtUtc);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPersonPositionsAsync_WhitespacePersonId_ThrowsArgumentException()
    {
        // Arrange
        const string personId = "   ";

        // Act & Assert
        Func<Task> act = async () => await _service.GetPersonPositionsAsync(personId, TokenExpiresAtUtc);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region Ordering Contract Tests

    /// <summary>Pins ordinal ordering, independent of the InvariantGlobalization setting.</summary>
    [Fact]
    public async Task GetAllDepartmentsAsync_OrdersOrdinally_NotLinguistically()
    {
        // Arrange - under ordinal ordering every uppercase letter sorts before every lowercase one
        const int divisionId = 1;
        List<DepartmentModel> models =
        [
            new DepartmentModel { DepartmentId = 1, DepartmentName = "apple", DivisionId = divisionId },
            new DepartmentModel { DepartmentId = 2, DepartmentName = "Zebra", DivisionId = divisionId },
            new DepartmentModel { DepartmentId = 3, DepartmentName = "Apple", DivisionId = divisionId },
            new DepartmentModel { DepartmentId = 4, DepartmentName = "banana", DivisionId = divisionId }
        ];

        _mockCoreService
            .Setup(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _service.GetAllDepartmentsAsync(divisionId);

        // Assert
        result.Select(d => d.DepartmentName).Should().ContainInOrder("Apple", "Zebra", "apple", "banana");
    }

    [Fact]
    public async Task GetPersonPositionsAsync_OrdersOrdinally_NotLinguistically()
    {
        // Arrange
        const string personId = "person-1";
        List<PersonPositionModel> models =
        [
            new PersonPositionModel { EntityName = "analyst" },
            new PersonPositionModel { EntityName = "Zookeeper" },
            new PersonPositionModel { EntityName = "Analyst" }
        ];

        _mockCoreService
            .Setup(s => s.GetPersonPositionsAsync(personId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _service.GetPersonPositionsAsync(personId, TokenExpiresAtUtc);

        // Assert
        result.Select(p => p.EntityName).Should().ContainInOrder("Analyst", "Zookeeper", "analyst");
    }

    #endregion

    #region Cache Immutability Tests

    /// <summary>Results are read-only: the L1 cache shares the instance across requests.</summary>
    [Fact]
    public async Task GetAllDepartmentsAsync_ReturnsImmutableCollection()
    {
        // Arrange
        const int divisionId = 1;
        List<DepartmentModel> models =
        [
            new DepartmentModel { DepartmentId = 1, DepartmentName = "Alpha", DivisionId = divisionId },
            new DepartmentModel { DepartmentId = 2, DepartmentName = "Beta", DivisionId = divisionId }
        ];

        _mockCoreService
            .Setup(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _service.GetAllDepartmentsAsync(divisionId);

        // Assert - the caller cannot mutate the shared cache entry
        IList<DepartmentDto> asMutable = (IList<DepartmentDto>)result;
        Action add = () => asMutable.Add(new DepartmentDto { DepartmentId = 99, DepartmentName = "Injected", DepartmentCode = "INJ" });
        Action clear = asMutable.Clear;
        add.Should().Throw<NotSupportedException>();
        clear.Should().Throw<NotSupportedException>();

        // And the next request still sees the untouched entry
        var second = await _service.GetAllDepartmentsAsync(divisionId);
        second.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllRolesAsync_ReturnsImmutableCollection()
    {
        // Arrange
        const int divisionId = 1;
        List<RoleModel> models =
        [
            new RoleModel { Id = 1, Name = "Alpha Role", DivisionId = divisionId }
        ];

        _mockCoreService
            .Setup(s => s.GetAllRolesAsync(divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var result = await _service.GetAllRolesAsync(divisionId);

        // Assert
        Action add = () => ((IList<RoleDto>)result).Add(new RoleDto { Id = 99, Name = "Injected" });
        add.Should().Throw<NotSupportedException>();
    }

    #endregion
}
