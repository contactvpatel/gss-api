namespace GSS.Core.Helpers;

/// <summary>
/// Limits of the <c>Idempotency-Key</c> header: how long a stored response is replayed and what a key may look like.
/// </summary>
public static class IdempotencyLimits
{
    /// <summary>Request header carrying the key.</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Response header set to <c>true</c> on a replayed response.</summary>
    public const string ReplayedHeaderName = "Idempotent-Replayed";

    /// <summary>Longest accepted key.</summary>
    public const int MaxKeyLength = 255;

    /// <summary>How long a response is replayed; older rows are ignored and then deleted.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>The message for a key that is empty, too long or not printable ASCII.</summary>
    public static readonly string InvalidKey = $"{HeaderName} must be 1 to {MaxKeyLength} printable ASCII characters, without spaces.";

    /// <summary>True for 1 to <see cref="MaxKeyLength"/> printable ASCII characters (no space, no control characters).</summary>
    public static bool IsValidKey(string? key)
    {
        return !string.IsNullOrEmpty(key)
            && key.Length <= MaxKeyLength
            && key.All(c => c is > ' ' and <= '~');
    }
}
