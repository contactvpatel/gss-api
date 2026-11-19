using System.Text.RegularExpressions;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Masks JWTs, secret-named values, card numbers and SSNs in log and error text.
/// </summary>
public static class SensitiveDataSanitizer
{
    // JWTs only (a JWT header always starts "eyJ"; signature may be empty). Ids, GUIDs and hosts stay readable.
    private static readonly Regex TokenPattern = new(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*", RegexOptions.Compiled);
    // JSON ("password":"x"), key=value and key: value forms. A key ending in a secret word matches with any
    // prefix (access_token, clientSecret, x-api-key); bare "pass" only as the whole key, so "compass" is kept.
    // Values stop at } and & so a JSON object or form body is not swallowed.
    private static readonly Regex PasswordPattern = new(@"[""']?\b(?:[\w-]{0,40}?(?:password|passwd|pwd|secret|token|api[_-]?key)|pass)[""']?\s*[:=]\s*[""']?([^\s""',;}&]+)[""']?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CreditCardPattern = new(@"\b(?:\d{4}[-\s]?){3}\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex SsnPattern = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);

    /// <summary>Returns the input with sensitive values masked.</summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input ?? string.Empty;
        }

        string sanitized = input;

        // Fully masked: an unsigned SSO token's last characters are claims.
        sanitized = TokenPattern.Replace(sanitized, "***");

        sanitized = PasswordPattern.Replace(sanitized, match =>
        {
            string key = match.Groups[0].Value;
            return key.Split(':', '=')[0] + ": ***";
        });

        sanitized = CreditCardPattern.Replace(sanitized, "****-****-****-****");

        sanitized = SsnPattern.Replace(sanitized, "***-**-****");

        return sanitized;
    }
}
