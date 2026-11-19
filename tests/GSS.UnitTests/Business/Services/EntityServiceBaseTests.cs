using FluentAssertions;
using GSS.Business.Models;
using GSS.Business.Services.Base;
using GSS.Core.Interfaces.Base;
using GSS.UnitTests.Common;
using GSS.UnitTests.Common.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GSS.UnitTests.Business.Services;

[Trait("Category", TestCategories.Unit)]
public class EntityServiceBaseTests
{
    private readonly Mock<IRepository<TestEntity>> _repository = new();

    public EntityServiceBaseTests()
    {
        // The writes report whether they hit their rows; true unless a test simulates a concurrent delete.
        _repository.Setup(repository => repository.UpdateAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repository.Setup(repository => repository.UpdateRangeAsync(It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task CreateAsync_NullDto_ThrowsArgumentNullException()
    {
        TestCrudService service = CreateService();

        Func<Task> act = async () => await service.CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetByIdAsync_MissingEntity_ReturnsNotFound()
    {
        _repository.Setup(repository => repository.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestEntity?)null);
        TestCrudService service = CreateService();

        Result<TestEntityDto> result = await service.GetByIdAsync(42);

        result.IsNotFound.Should().BeTrue();
    }

    [Fact]
    public async Task CreateBatchAsync_EmptyInput_ReturnsEmptyResult()
    {
        TestCrudService service = CreateService();

        Result<IReadOnlyList<TestEntityDto>> result = await service.CreateBatchAsync([]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ConflictFromHook_ReturnsConflictWithoutWriting()
    {
        TestCrudService service = CreateService(createViolation: RuleViolation.Conflict("Name already exists."));

        Result<TestEntityDto> result = await service.CreateAsync(new CreateTestEntityDto());

        result.IsConflict.Should().BeTrue();
        _repository.Verify(repository => repository.AddAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBatchAsync_ConflictFromHook_ReturnsConflictWithoutWriting()
    {
        TestCrudService service = CreateService(createViolation: RuleViolation.Conflict("Name already exists."));

        Result<IReadOnlyList<TestEntityDto>> result = await service.CreateBatchAsync([new CreateTestEntityDto()]);

        result.IsConflict.Should().BeTrue();
        _repository.Verify(repository => repository.AddRangeAsync(It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_InvalidViolation_ReturnsFailureWithoutWriting()
    {
        TestCrudService service = CreateService(createViolation: RuleViolation.Invalid("Reference not found."));

        Result<TestEntityDto> result = await service.CreateAsync(new CreateTestEntityDto());

        result.IsFailure.Should().BeTrue();
        result.IsConflict.Should().BeFalse();
        result.ErrorMessage.Should().Be("Reference not found.");
        _repository.Verify(repository => repository.AddAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_OnCreatingInitializesServerOwnedFieldsBeforeWriting()
    {
        TestCrudService service = CreateService(createdNamePrefix: "server-");

        Result<TestEntityDto> result = await service.CreateAsync(new CreateTestEntityDto { Name = "value" });

        result.Value.Name.Should().Be("server-value");
        _repository.Verify(repository => repository.AddAsync(
            It.Is<TestEntity>(entity => entity.Name == "server-value"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateBatchAsync_OnCreatingInitializesEveryEntityBeforeWriting()
    {
        TestCrudService service = CreateService(createdNamePrefix: "server-");

        Result<IReadOnlyList<TestEntityDto>> result = await service.CreateBatchAsync(
            [new CreateTestEntityDto { Name = "one" }, new CreateTestEntityDto { Name = "two" }]);

        result.Value.Select(dto => dto.Name).Should().Equal("server-one", "server-two");
        _repository.Verify(repository => repository.AddRangeAsync(
            It.Is<IReadOnlyList<TestEntity>>(entities => entities.All(entity => entity.Name.StartsWith("server-"))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_AlreadyDeletedEntity_ReturnsTrue()
    {
        _repository.Setup(repository => repository.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestEntity?)null);
        _repository.Setup(repository => repository.ExistsAsync(7, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        TestCrudService service = CreateService();

        Result<int> result = await service.DeleteAsync(7);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(7);
    }

    [Fact]
    public async Task DeleteBatchAsync_AllActive_DeletesInOneRangeCallAndReturnsEveryRequestedId()
    {
        List<TestEntity> found = [new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "b" }];
        _repository.Setup(repository => repository.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(found);
        TestCrudService service = CreateService();

        Result<IReadOnlyList<int>> result = await service.DeleteBatchAsync([1, 2]);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(1, 2);
        _repository.Verify(repository => repository.DeleteRangeAsync(
            It.Is<IReadOnlyList<TestEntity>>(entities => entities.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(repository => repository.DeleteAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteBatchAsync_DuplicateIds_ReturnsFailureAndDoesNothing()
    {
        TestCrudService service = CreateService();

        Result<IReadOnlyList<int>> result = await service.DeleteBatchAsync([1, 2, 1]);
        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Duplicate").And.Contain("1").And.NotContain("2");
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteBatchAsync_IdThatNeverExisted_ReturnsNotFoundAndDeletesNothing()
    {
        _repository.Setup(repository => repository.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TestEntity { Id = 1, Name = "a" }]);
        _repository.Setup(repository => repository.FindExistingIdsAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 2, 3 })), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([2]);
        TestCrudService service = CreateService();

        Result<IReadOnlyList<int>> result = await service.DeleteBatchAsync([1, 2, 3]);
        result.IsNotFound.Should().BeTrue();
        result.ErrorMessage.Should().Contain("IDs: 3.");
        _repository.Verify(repository => repository.DeleteRangeAsync(
            It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteBatchAsync_AlreadyDeletedId_SucceedsAndDeletesOnlyActiveRows()
    {
        _repository.Setup(repository => repository.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TestEntity { Id = 1, Name = "a" }]);
        _repository.Setup(repository => repository.FindExistingIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([2]);
        TestCrudService service = CreateService();

        Result<IReadOnlyList<int>> result = await service.DeleteBatchAsync([1, 2]);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(1, 2);
        _repository.Verify(repository => repository.DeleteRangeAsync(
            It.Is<IReadOnlyList<TestEntity>>(entities => entities.Count == 1 && entities[0].Id == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteBatchAsync_AllAlreadyDeleted_SucceedsWithoutWriting()
    {
        _repository.Setup(repository => repository.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repository.Setup(repository => repository.FindExistingIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2]);
        TestCrudService service = CreateService();

        Result<IReadOnlyList<int>> result = await service.DeleteBatchAsync([1, 2]);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(1, 2);
        _repository.Verify(repository => repository.DeleteRangeAsync(
            It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteBatchAsync_EmptyInput_ReturnsEmptySuccess()
    {
        TestCrudService service = CreateService();

        Result<IReadOnlyList<int>> result = await service.DeleteBatchAsync([]);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateBatchAsync_EmptyInput_ReturnsEmptySuccess()
    {
        TestCrudService service = CreateService();

        Result<IReadOnlyList<TestEntityDto>> result = await service.UpdateBatchAsync([]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateBatchAsync_AllFound_UpdatesInOneRangeCall()
    {
        _repository.Setup(repository => repository.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TestEntity { Id = 1, Name = "a" }, new TestEntity { Id = 2, Name = "b" }]);
        TestCrudService service = CreateService();

        Result<IReadOnlyList<TestEntityDto>> result = await service.UpdateBatchAsync(
            [(1, new UpdateTestEntityDto { Name = "a2" }), (2, new UpdateTestEntityDto { Name = "b2" })]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(dto => dto.Name).Should().Equal("a2", "b2");
        _repository.Verify(repository => repository.UpdateRangeAsync(
            It.Is<IReadOnlyList<TestEntity>>(entities => entities.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_RowDeletedConcurrently_ReturnsNotFound()
    {
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestEntity { Id = 1, Name = "before" });
        _repository.Setup(repository => repository.UpdateAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        TestCrudService service = CreateService();

        Result<TestEntityDto> result = await service.UpdateAsync(1, new UpdateTestEntityDto { Name = "after" });

        result.IsNotFound.Should().BeTrue("the delete came first");
    }

    [Fact]
    public async Task UpdateBatchAsync_RowsDeletedConcurrently_ReturnsNotFoundNamingThem()
    {
        _repository.Setup(repository => repository.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TestEntity { Id = 1, Name = "a" }, new TestEntity { Id = 2, Name = "b" }]);
        _repository.Setup(repository => repository.UpdateRangeAsync(It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repository.Setup(repository => repository.FindExistingIdsAsync(It.IsAny<IReadOnlyList<int>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([1]);
        TestCrudService service = CreateService();

        Result<IReadOnlyList<TestEntityDto>> result = await service.UpdateBatchAsync(
            [(1, new UpdateTestEntityDto { Name = "a2" }), (2, new UpdateTestEntityDto { Name = "b2" })]);

        result.IsNotFound.Should().BeTrue();
        result.ErrorMessage.Should().Contain("2").And.NotContain("1,");
    }

    [Fact]
    public async Task UpdateBatchAsync_NothingWrittenButAllStillActive_ReturnsConflict()
    {
        _repository.Setup(repository => repository.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TestEntity { Id = 1, Name = "a" }]);
        _repository.Setup(repository => repository.UpdateRangeAsync(It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repository.Setup(repository => repository.FindExistingIdsAsync(It.IsAny<IReadOnlyList<int>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([1]);
        TestCrudService service = CreateService();

        Result<IReadOnlyList<TestEntityDto>> result = await service.UpdateBatchAsync([(1, new UpdateTestEntityDto { Name = "a2" })]);

        result.IsConflict.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateBatchAsync_MissingIds_ReturnsNotFoundListingThemWithoutWriting()
    {
        _repository.Setup(repository => repository.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TestEntity { Id = 1, Name = "a" }, new TestEntity { Id = 2, Name = "b" }]);
        TestCrudService service = CreateService();

        Result<IReadOnlyList<TestEntityDto>> result = await service.UpdateBatchAsync(
            [(1, new UpdateTestEntityDto { Name = "a2" }), (4, new UpdateTestEntityDto { Name = "d" }), (2, new UpdateTestEntityDto { Name = "b2" }), (3, new UpdateTestEntityDto { Name = "c" })]);

        result.IsNotFound.Should().BeTrue("the batch is all or nothing");
        result.ErrorMessage.Should().Contain("4, 3");
        _repository.Verify(repository => repository.UpdateRangeAsync(It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateBatchAsync_DuplicateIds_ReturnsFailureWithoutReadingOrWriting()
    {
        TestCrudService service = CreateService();

        Result<IReadOnlyList<TestEntityDto>> result = await service.UpdateBatchAsync(
            [(1, new UpdateTestEntityDto { Name = "a" }), (2, new UpdateTestEntityDto { Name = "b" }), (1, new UpdateTestEntityDto { Name = "a2" })]);

        result.IsFailure.Should().BeTrue();
        result.IsConflict.Should().BeFalse();
        result.ErrorMessage.Should().Contain("1").And.NotContain("2");
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_ValidationFailureLeavesEntityUnchangedAndWritesNothing()
    {
        TestEntity entity = new() { Id = 1, Name = "before" };
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        TestCrudService service = CreateService(updateViolation: RuleViolation.Invalid("Change rejected."));

        Result<TestEntityDto> result = await service.UpdateAsync(1, new UpdateTestEntityDto { Name = "after" });

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("Change rejected.");
        entity.Name.Should().Be("before", "validation runs before the update is applied");
        _repository.Verify(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(repository => repository.UpdateAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PatchAsync_ValidationFailureLeavesEntityUnchangedAndWritesNothing()
    {
        TestEntity entity = new() { Id = 1, Name = "before" };
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        TestCrudService service = CreateService(updateViolation: RuleViolation.Invalid("Change rejected."));

        Result<TestEntityDto> result = await service.PatchAsync(1, new UpdateTestEntityDto { Name = "after" });

        result.IsFailure.Should().BeTrue();
        entity.Name.Should().Be("before", "validation runs before the patch is applied");
        _repository.Verify(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(repository => repository.UpdateAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateBatchAsync_ValidationFailureLeavesEveryEntityUnchangedAndWritesNothing()
    {
        TestEntity first = new() { Id = 1, Name = "one" };
        TestEntity second = new() { Id = 2, Name = "two" };
        _repository.Setup(repository => repository.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        TestCrudService service = CreateService(updateViolation: RuleViolation.Invalid("Batch rejected."));

        Result<IReadOnlyList<TestEntityDto>> result = await service.UpdateBatchAsync(
            [(1, new UpdateTestEntityDto { Name = "changed-one" }), (2, new UpdateTestEntityDto { Name = "changed-two" })]);

        result.IsFailure.Should().BeTrue();
        first.Name.Should().Be("one");
        second.Name.Should().Be("two");
        _repository.Verify(repository => repository.UpdateRangeAsync(
            It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindMissingAsync_ReturnsDistinctMissingIdsInRequestOrder()
    {
        _repository.Setup(repository => repository.FindExistingIdsAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 3, 1, 2 })),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([2]);
        TestCrudService service = CreateService();

        int[] missing = await service.FindMissingForTestAsync([3, 1, 3, 2]);

        missing.Should().Equal(3, 1);
    }

    private TestCrudService CreateService(
        RuleViolation? createViolation = null,
        RuleViolation? updateViolation = null,
        string? createdNamePrefix = null)
    {
        return new(_repository.Object, createViolation, updateViolation, createdNamePrefix);
    }

    private sealed class TestCrudService(
        IRepository<TestEntity> repository,
        RuleViolation? createViolation,
        RuleViolation? updateViolation,
        string? createdNamePrefix)
        : EntityServiceBase<TestEntity, TestEntityDto, CreateTestEntityDto, UpdateTestEntityDto>(
            repository,
            NullLogger.Instance)
    {
        protected override string EntityName => "TestEntity";

        protected override Task<RuleViolation?> ValidateCreateAsync(
            IReadOnlyList<CreateTestEntityDto> createDtos,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(createViolation);
        }

        protected override Task<RuleViolation?> ValidateUpdateAsync(
            IReadOnlyList<(TestEntity Current, UpdateTestEntityDto UpdateDto)> updates,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(updateViolation);
        }

        protected override void OnCreating(TestEntity entity, CreateTestEntityDto createDto)
        {
            if (createdNamePrefix != null)
            {
                entity.Name = createdNamePrefix + entity.Name;
            }
        }

        public Task<int[]> FindMissingForTestAsync(IEnumerable<int> ids)
        {
            return FindMissingAsync(Repository, ids, CancellationToken.None);
        }

    }
}
