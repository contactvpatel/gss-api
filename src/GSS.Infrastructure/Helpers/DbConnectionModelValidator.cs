using System.ComponentModel.DataAnnotations;
using GSS.Util;
using GSS.Util.Models;
using Microsoft.Extensions.Options;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Validates <c>DbConnectionSettings</c> and its Read and Write connections (DataAnnotations do not recurse into
/// them on their own). Failures name the full key, e.g. <c>DbConnectionSettings:Write:Host</c>.
/// </summary>
public sealed class DbConnectionModelValidator : IValidateOptions<DbConnectionModel>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DbConnectionModel options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<ValidationResult> results = [];
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        List<string> failures =
        [
            .. results.Select(r => $"{ConfigurationKeys.DbConnectionSettings}:{string.Join(",", r.MemberNames)}: {r.ErrorMessage}"),
            .. Errors(nameof(options.Read), options.Read),
            .. Errors(nameof(options.Write), options.Write)
        ];
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> Errors(string side, ConnectionModel? connection)
    {
        string prefix = $"{ConfigurationKeys.DbConnectionSettings}:{side}";
        if (connection is null)
        {
            return [$"{prefix} is required."];
        }

        List<ValidationResult> results = [];
        Validator.TryValidateObject(connection, new ValidationContext(connection), results, validateAllProperties: true);
        List<string> errors = [.. results.Select(r => $"{prefix}:{string.Join(",", r.MemberNames)}: {r.ErrorMessage}")];

        // Compared after defaults, as Npgsql sees them (e.g. MinPoolSize 200 with MaxPoolSize unset).
        int maxPoolSize = connection.MaxPoolSize ?? DatabaseConnectionStrings.DefaultMaxPoolSize;
        int minPoolSize = connection.MinPoolSize ?? DatabaseConnectionStrings.DefaultMinPoolSize;
        if (minPoolSize > maxPoolSize)
        {
            errors.Add($"{prefix}:{nameof(ConnectionModel.MinPoolSize)}: MinPoolSize ({minPoolSize}) must not exceed MaxPoolSize ({maxPoolSize}).");
        }

        return errors;
    }
}
