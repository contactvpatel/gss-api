using FluentAssertions;
using GSS.Api.HostedServices;
using GSS.Core.Helpers;
using GSS.Core.Interfaces;
using GSS.UnitTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GSS.UnitTests.API.HostedServices;

/// <summary>
/// Unit tests for IdempotencyCleanupHostedService.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class IdempotencyCleanupHostedServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private const int Batch = IdempotencyCleanupHostedService.BatchSize;

    private readonly Mock<IIdempotencyRepository> _repository = new();
    private readonly IdempotencyCleanupHostedService _service;

    public IdempotencyCleanupHostedServiceTests()
    {
        var provider = new ServiceCollection().AddSingleton(_repository.Object).BuildServiceProvider();
        _service = new IdempotencyCleanupHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(), new ManualTimeProvider(Now), Mock.Of<ILogger<IdempotencyCleanupHostedService>>());
    }

    [Fact]
    public async Task DeleteExpiredAsync_DeletesRowsOlderThanTheRetention()
    {
        DateTime? cutoff = null;
        _repository.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), Batch, It.IsAny<CancellationToken>()))
            .Callback<DateTime, int, CancellationToken>((c, _, _) => cutoff = c)
            .ReturnsAsync(0);

        await _service.DeleteExpiredAsync(CancellationToken.None);

        cutoff.Should().Be((Now - IdempotencyLimits.Retention).UtcDateTime);
    }

    [Fact]
    public async Task DeleteExpiredAsync_KeepsDeletingWhileBatchesComeBackFull()
    {
        _repository.SetupSequence(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), Batch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Batch).ReturnsAsync(Batch).ReturnsAsync(12);

        int total = await _service.DeleteExpiredAsync(CancellationToken.None);

        total.Should().Be(Batch * 2 + 12);
        _repository.Verify(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), Batch, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task DeleteExpiredAsync_WithNothingExpired_RunsOneDelete()
    {
        _repository.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), Batch, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        int total = await _service.DeleteExpiredAsync(CancellationToken.None);

        total.Should().Be(0);
        _repository.Verify(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), Batch, It.IsAny<CancellationToken>()), Times.Once);
    }
}
