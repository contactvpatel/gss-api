namespace GSS.UnitTests.Common;

/// <summary>TimeProvider whose UTC now is set by the test.</summary>
public sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    /// <summary>Current UTC time returned by <see cref="GetUtcNow"/>.</summary>
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }
}
