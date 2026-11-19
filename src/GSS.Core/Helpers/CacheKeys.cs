using System.Security.Cryptography;
using System.Text;

namespace GSS.Core.Helpers;

/// <summary>
/// Cache key builders. Include every parameter that changes the cached result.
/// </summary>
public static class CacheKeys
{
    /// <summary>Prefix for ASM security entries (keyed by token hash).</summary>
    public const string AsmSecurityPrefix = "asm-security-";

    /// <summary>Prefix for SSO token validation entries (keyed by token hash).</summary>
    public const string TokenValidationPrefix = "token-validation-";

    /// <summary>Person positions key: <c>person-{personId}-positions</c>.</summary>
    public const string PersonPositionsPrefix = "person-";

    /// <summary>Suffix for <see cref="PersonPositions"/>.</summary>
    public const string PersonPositionsSuffix = "-positions";

    /// <summary>ASM security key for a bearer token: <c>asm-security-{sha256}</c>.</summary>
    public static string AsmSecurity(string token)
    {
        return $"{AsmSecurityPrefix}{TokenHash(token)}";
    }

    /// <summary>SSO token validation key for a bearer token: <c>token-validation-{sha256}</c>.</summary>
    public static string TokenValidation(string token)
    {
        return $"{TokenValidationPrefix}{TokenHash(token)}";
    }

    /// <summary>Lower-case hex SHA-256 of the token, so a raw token never appears in a key.</summary>
    private static string TokenHash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token, nameof(token));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>MIS positions key for a person.</summary>
    public static string PersonPositions(string personId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personId, nameof(personId));
        return $"{PersonPositionsPrefix}{personId}{PersonPositionsSuffix}";
    }

    /// <summary>Cache key for every MIS department (all divisions).</summary>
    public const string AllDepartments = "departments";

    /// <summary>MIS role types key for a division.</summary>
    public static string RoleTypes(int divisionId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(divisionId, 1, nameof(divisionId));
        return $"role-types-division-{divisionId}";
    }

    /// <summary>MIS roles key for a division.</summary>
    public static string Roles(int divisionId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(divisionId, 1, nameof(divisionId));
        return $"roles-division-{divisionId}";
    }

    /// <summary>MIS role key for one role id.</summary>
    public static string RoleById(int roleId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(roleId, 1, nameof(roleId));
        return $"role-{roleId}";
    }

    /// <summary>MIS roles key for a department.</summary>
    public static string RolesByDepartment(int departmentId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(departmentId, 1, nameof(departmentId));
        return $"roles-department-{departmentId}";
    }
}
