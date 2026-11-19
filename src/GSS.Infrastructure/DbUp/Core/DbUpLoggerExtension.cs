using System.Globalization;
using DbUp.Engine.Output;
using GSS.Infrastructure.Helpers;
using Microsoft.Extensions.Logging;

namespace GSS.Infrastructure.DbUp.Core;

/// <summary>
/// DbUp log output through <see cref="ILogger"/>. DbUp passes composite-format strings, so each line is formatted
/// here (only when the level is on) and logged as the value of a constant template, never as the template itself.
/// </summary>
public class DbUpLoggerExtension(ILogger logger) : IUpgradeLog
{
    /// <inheritdoc />
    public void LogTrace(string format, params object[] args) => Write(LogLevel.Trace, null, format, args);

    /// <inheritdoc />
    public void LogDebug(string format, params object[] args) => Write(LogLevel.Debug, null, format, args);

    /// <inheritdoc />
    public void LogInformation(string format, params object[] args) => Write(LogLevel.Information, null, format, args);

    /// <inheritdoc />
    public void LogWarning(string format, params object[] args) => Write(LogLevel.Warning, null, format, args);

    /// <inheritdoc />
    public void LogError(string format, params object[] args) => Write(LogLevel.Error, null, format, args);

    /// <inheritdoc />
    public void LogError(Exception ex, string format, params object[] args) => Write(LogLevel.Error, ex, format, args);

    private void Write(LogLevel level, Exception? exception, string format, object[] args)
    {
        if (logger.IsEnabled(level))
        {
            logger.DbUp(level, exception, SafeFormat(format, args));
        }
    }

    /// <summary>Formats DbUp's composite format; a message with stray braces (SQL, exception text) is kept as-is.</summary>
    internal static string SafeFormat(string format, object[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return format;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Concat(format, " ", string.Join(" ", args));
        }
    }
}
