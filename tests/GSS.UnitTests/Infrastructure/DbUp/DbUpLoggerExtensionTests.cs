using FluentAssertions;
using GSS.Infrastructure.DbUp.Core;
using GSS.UnitTests.Common;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSS.UnitTests.Infrastructure.DbUp;

/// <summary>
/// DbUp lines are formatted here and logged under one constant template, so SQL braces never act as placeholders.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DbUpLoggerExtensionTests
{
    private readonly CapturingLogger _logger = new(LogLevel.Information);

    [Fact]
    public void LogInformation_FormatsArgs_UnderAConstantTemplate()
    {
        new DbUpLoggerExtension(_logger).LogInformation("Executing Database Server script '{0}'", "ALM-001.sql");

        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Be("Executing Database Server script 'ALM-001.sql'");
        entry.Template.Should().Be("{DbUpMessage}");
    }

    [Theory]
    [InlineData("DO $$ BEGIN IF x THEN {raise} END IF; END $$")]
    [InlineData("Bad format {0} {1} {")]
    public void LogWarning_MessageWithBraces_IsLoggedAsIs(string sqlLikeMessage)
    {
        new DbUpLoggerExtension(_logger).LogWarning(sqlLikeMessage, "arg");

        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Message.Should().Be(sqlLikeMessage + " arg");
        entry.Template.Should().Be("{DbUpMessage}");
    }

    [Fact]
    public void LogError_WithException_KeepsTheException()
    {
        InvalidOperationException exception = new("boom");

        new DbUpLoggerExtension(_logger).LogError(exception, "Script {0} failed", "x.sql");

        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Exception.Should().BeSameAs(exception);
        entry.Message.Should().Be("Script x.sql failed");
    }

    [Fact]
    public void LevelBelowMinimum_LogsNothing()
    {
        new DbUpLoggerExtension(_logger).LogDebug("Checking whether journal table exists {0}", "x");

        _logger.Entries.Should().BeEmpty();
    }

    private sealed record CapturedEntry(LogLevel Level, string Message, string? Template, Exception? Exception);

    private sealed class CapturingLogger(LogLevel minimum) : ILogger
    {
        public List<CapturedEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string? template = (state as IEnumerable<KeyValuePair<string, object?>>)?
                .FirstOrDefault(kv => kv.Key == "{OriginalFormat}").Value as string;
            Entries.Add(new CapturedEntry(logLevel, formatter(state, exception), template, exception));
        }
    }
}
