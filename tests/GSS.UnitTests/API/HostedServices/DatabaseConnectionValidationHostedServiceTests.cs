using FluentAssertions;
using GSS.Api.HostedServices;
using GSS.Infrastructure.Interfaces;
using GSS.UnitTests.Common;
using GSS.Util.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GSS.UnitTests.API.HostedServices;

/// <summary>
/// Unit tests for DatabaseConnectionValidationHostedService.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DatabaseConnectionValidationHostedServiceTests
{
    [Fact]
    public void Constructor_AcceptsDependencies()
    {
        // Arrange & Act
        Mock<IDapperConnectionFactory> mockConnectionFactory = new();
        Mock<ILogger<DatabaseConnectionValidationHostedService>> mockLogger = new();
        DatabaseConnectionValidationHostedService service = new(
            mockConnectionFactory.Object,
            Options.Create(new DbConnectionModel()),
            mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }
}
