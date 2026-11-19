using FluentAssertions;
using GSS.Business.Services.Base;
using GSS.Core.Interfaces.Base;
using GSS.UnitTests.Common;
using GSS.UnitTests.Common.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GSS.UnitTests.Business.Services;

[Trait("Category", TestCategories.Unit)]
public class EntityServiceLogTests
{
    [Fact]
    public async Task Create_LogsOneStructuredLine()
    {
        Mock<IRepository<TestEntity>> repository = new();
        repository.Setup(value => value.AddAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TestEntity, CancellationToken>((entity, _) => entity.Id = 5)
            .ReturnsAsync((TestEntity entity, CancellationToken _) => entity);
        CapturingLogger logger = new();
        TestService service = new(repository.Object, logger);

        await service.CreateAsync(new CreateTestEntityDto { Name = "value" });

        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.EventId.Should().Be(3000);
        entry.Message.Should().Be("TestEntity 5 created");
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("EntityId", 5));
    }

    [Fact]
    public async Task DeleteBatch_WithAlreadyDeletedEntity_LogsOneSummary()
    {
        Mock<IRepository<TestEntity>> repository = new();
        repository.Setup(value => value.FindByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TestEntity { Id = 5 }]);
        repository.Setup(value => value.FindExistingIdsAsync(It.IsAny<IReadOnlyList<int>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([7]);
        CapturingLogger logger = new();
        TestService service = new(repository.Object, logger);

        var result = await service.DeleteBatchAsync([5, 7]);

        result.IsSuccess.Should().BeTrue();
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.EventId.Should().Be(3012);
        entry.Message.Should().Be("Batch deleted 2 TestEntity: 1 already deleted");
    }

    private sealed class TestService(IRepository<TestEntity> repository, ILogger logger)
        : EntityServiceBase<TestEntity, TestEntityDto, CreateTestEntityDto, UpdateTestEntityDto>(repository, logger)
    {
        protected override string EntityName => "TestEntity";
    }

    private sealed record LogEntry(int EventId, string Message, IReadOnlyList<KeyValuePair<string, object?>> Properties);

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            IReadOnlyList<KeyValuePair<string, object?>> properties = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            Entries.Add(new LogEntry(eventId.Id, formatter(state, exception), properties));
        }
    }
}
