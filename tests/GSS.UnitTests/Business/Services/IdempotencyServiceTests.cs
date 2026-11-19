using FluentAssertions;
using GSS.Business.Models;
using GSS.Business.Services;
using GSS.Core.Helpers;
using GSS.Core.Interfaces;
using GSS.Core.Interfaces.Base;
using GSS.Core.Models;
using GSS.UnitTests.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GSS.UnitTests.Business.Services;

/// <summary>
/// Unit tests for IdempotencyService.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class IdempotencyServiceTests
{
    private const string Key = "key-1";
    private const string Hash = "hash-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IIdempotencyRepository> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IUserContext> _userContext = new();
    private readonly List<string> _calls = [];
    private readonly IdempotencyService _service;

    public IdempotencyServiceTests()
    {
        _userContext.Setup(u => u.GetUserId()).Returns("7");
        _unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<System.Data.IsolationLevel>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask).Callback(() => _calls.Add("begin"));
        _unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Callback(() => _calls.Add("commit"));
        _unitOfWork.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Callback(() => _calls.Add("rollback"));
        _repository.Setup(r => r.LockKeyAsync(7, Key, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Callback(() => _calls.Add("lock"));
        _repository.Setup(r => r.SaveAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask).Callback(() => _calls.Add("save"));

        _service = new IdempotencyService(_repository.Object, _unitOfWork.Object, _userContext.Object,
            new ManualTimeProvider(Now), Mock.Of<ILogger<IdempotencyService>>());
    }

    [Fact]
    public async Task BeginAsync_FirstUseOfTheKey_ProceedsWithTheTransactionOpen()
    {
        _repository.Setup(r => r.FindAsync(7, Key, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyRecord?)null);

        var start = await _service.BeginAsync(Key, Hash);

        start.Outcome.Should().Be(IdempotencyOutcome.Proceed);
        _calls.Should().Equal("begin", "lock");
    }

    [Fact]
    public async Task BeginAsync_ReadsOnlyResponsesWithinTheRetention()
    {
        DateTime? savedAfter = null;
        _repository.Setup(r => r.FindAsync(7, Key, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, DateTime, CancellationToken>((_, _, after, _) => savedAfter = after)
            .ReturnsAsync((IdempotencyRecord?)null);

        await _service.BeginAsync(Key, Hash);

        savedAfter.Should().Be((Now - IdempotencyLimits.Retention).UtcDateTime);
    }

    [Fact]
    public async Task BeginAsync_SameRequestAgain_RepliesWithTheStoredResponseAndReleasesTheKey()
    {
        _repository.Setup(r => r.FindAsync(7, Key, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyRecord(7, Key, Hash, 201, "{\"a\":1}", "/api/v1/customers/5"));

        var start = await _service.BeginAsync(Key, Hash);

        start.Outcome.Should().Be(IdempotencyOutcome.Replay);
        start.Response.Should().Be(new StoredResponse(201, "{\"a\":1}", "/api/v1/customers/5"));
        _calls.Should().Equal("begin", "lock", "rollback");
    }

    [Fact]
    public async Task BeginAsync_DifferentRequestWithTheSameKey_IsAMismatchAndReleasesTheKey()
    {
        _repository.Setup(r => r.FindAsync(7, Key, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyRecord(7, Key, "another-hash", 201, "{}", null));

        var start = await _service.BeginAsync(Key, Hash);

        start.Outcome.Should().Be(IdempotencyOutcome.Mismatch);
        start.Response.Should().BeNull();
        _calls.Should().Equal("begin", "lock", "rollback");
    }

    [Fact]
    public async Task BeginAsync_WhenTheLookupThrows_RollsBackAndRethrows()
    {
        _repository.Setup(r => r.FindAsync(7, Key, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

        var act = () => _service.BeginAsync(Key, Hash);

        await act.Should().ThrowAsync<TimeoutException>();
        _calls.Should().Equal("begin", "lock", "rollback");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0")]
    public async Task BeginAsync_WithoutANumericUser_Throws(string? userId)
    {
        _userContext.Setup(u => u.GetUserId()).Returns(userId);

        var act = () => _service.BeginAsync(Key, Hash);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _calls.Should().BeEmpty("no transaction is opened for a request nobody can own");
    }

    [Fact]
    public async Task CompleteAsync_SavesTheResponseThenCommits()
    {
        _repository.Setup(r => r.FindAsync(7, Key, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyRecord?)null);
        await _service.BeginAsync(Key, Hash);

        await _service.CompleteAsync(Key, Hash, new StoredResponse(201, "{}", "/x"));

        _calls.Should().Equal("begin", "lock", "save", "commit");
        _repository.Verify(r => r.SaveAsync(
            new IdempotencyRecord(7, Key, Hash, 201, "{}", "/x"), Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_WithoutBegin_Throws()
    {
        var act = () => _service.CompleteAsync(Key, Hash, new StoredResponse(201, "{}", null));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AbortAsync_AfterAFailedCommit_DoesNotRollBackAgain()
    {
        _repository.Setup(r => r.FindAsync(7, Key, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyRecord?)null);
        _unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        await _service.BeginAsync(Key, Hash);

        var act = () => _service.CompleteAsync(Key, Hash, new StoredResponse(201, "{}", null));
        await act.Should().ThrowAsync<TimeoutException>();
        await _service.AbortAsync();

        _unitOfWork.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never,
            "a failed commit has already ended the transaction");
    }

    [Fact]
    public async Task AbortAsync_WithTheTransactionOpen_RollsBackOnce()
    {
        _repository.Setup(r => r.FindAsync(7, Key, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyRecord?)null);
        await _service.BeginAsync(Key, Hash);

        await _service.AbortAsync();
        await _service.AbortAsync();

        _unitOfWork.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AbortAsync_WhenTheRollbackFails_DoesNotThrow()
    {
        _repository.Setup(r => r.FindAsync(7, Key, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyRecord?)null);
        _unitOfWork.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        await _service.BeginAsync(Key, Hash);

        var act = () => _service.AbortAsync();

        await act.Should().NotThrowAsync("the caller is already unwinding from the real failure");
    }
}
