namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Validates SQL identifiers interpolated into queries. Values must still be parameters.
/// </summary>
public static class SqlIdentifierValidator
{
    /// <summary>Returns the identifier (optionally without surrounding quotes) if it is letters, digits and underscores.</summary>
    /// <exception cref="ArgumentException">Blank or contains other characters.</exception>
    public static string Validate(string value, string paramName, bool trimQuotes = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier cannot be null or whitespace.", paramName);
        }

        string cleaned = trimQuotes ? value.Trim('"') : value;
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new ArgumentException("Identifier cannot be null or whitespace.", paramName);
        }

        if (cleaned.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
        {
            throw new ArgumentException(
                $"Identifier '{value}' contains invalid characters. Only alphanumeric and underscore are allowed.",
                paramName);
        }

        return cleaned;
    }
}
