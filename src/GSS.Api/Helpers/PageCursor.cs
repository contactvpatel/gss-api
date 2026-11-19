using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace GSS.Api.Helpers;

/// <summary>
/// Keyset paging cursor: the last returned ID, base64url-encoded so clients pass it back as an opaque token and the
/// sort key can change later without breaking them.
/// </summary>
public static class PageCursor
{
    /// <summary>Longest valid cursor (an int's digits, encoded).</summary>
    private const int MaxLength = 16;

    /// <summary>The cursor for the page after <paramref name="afterId"/>.</summary>
    public static string Encode(int afterId)
    {
        return Base64Url.EncodeToString(Encoding.ASCII.GetBytes(afterId.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The ID a cursor continues after; false for anything <see cref="Encode"/> did not produce.</summary>
    public static bool TryDecode(string? cursor, out int afterId)
    {
        afterId = 0;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxLength)
        {
            return false;
        }

        Span<byte> digits = stackalloc byte[MaxLength];
        return Base64Url.TryDecodeFromChars(cursor, digits, out int written)
            && int.TryParse(digits[..written], NumberStyles.None, CultureInfo.InvariantCulture, out afterId)
            && afterId > 0;
    }
}
