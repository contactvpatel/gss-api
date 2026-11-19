namespace GSS.Api.Helpers;

/// <summary>
/// Source-generated controller log messages. EventIds 2000+ (Infrastructure uses 1000-1499).
/// </summary>
internal static partial class ControllerLog
{
    /// <summary>A 404: a normal client outcome, so Debug. Same shape for every entity.</summary>
    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "{Entity} not found: {Identifier} {IdentifierValue}")]
    public static partial void NotFound(this ILogger logger, string entity, string identifier, string? identifierValue);
}
