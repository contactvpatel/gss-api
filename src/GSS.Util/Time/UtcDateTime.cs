namespace GSS.Util.Time;

/// <summary>UTC normalization for <see cref="DateTime"/> values crossing a boundary (HTTP, JSON, database).</summary>
public static class UtcDateTime
{
    /// <summary>Returns the value as UTC. <see cref="DateTimeKind.Unspecified"/> is treated as already UTC.</summary>
    public static DateTime Normalize(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
