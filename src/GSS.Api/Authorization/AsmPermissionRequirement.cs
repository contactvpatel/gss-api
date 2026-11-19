using Microsoft.AspNetCore.Authorization;

namespace GSS.Api.Authorization;

/// <summary>Met when the caller's selected position grants any one of <see cref="AnyOf"/>.</summary>
public sealed class AsmPermissionRequirement : IAuthorizationRequirement
{
    /// <summary>Creates a requirement satisfied by any of the given permissions.</summary>
    public AsmPermissionRequirement(IReadOnlyList<AsmPermission> anyOf)
    {
        ArgumentNullException.ThrowIfNull(anyOf);
        if (anyOf.Count == 0)
        {
            throw new ArgumentException("At least one permission is required.", nameof(anyOf));
        }

        AnyOf = anyOf;
    }

    /// <summary>Permissions, any one of which satisfies the requirement.</summary>
    public IReadOnlyList<AsmPermission> AnyOf { get; }

    /// <summary>Requirement satisfied by any of the given permissions (cross-module OR, for named policies).</summary>
    public static AsmPermissionRequirement Any(params AsmPermission[] permissions)
    {
        return new AsmPermissionRequirement(permissions);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Join(" | ", AnyOf.Select(p => $"{p.Module}:{p.Access}"));
    }
}
