using Microsoft.AspNetCore.Authorization;

namespace GSS.Api.Authorization;

/// <summary>
/// Requires an ASM permission on the caller's selected position. Several attributes = AND;
/// several access types in one attribute = OR.
/// </summary>
/// <param name="module">Module the permission applies to.</param>
/// <param name="access">Required access.</param>
/// <param name="orAccess">Alternative accesses, any of which also satisfies the requirement.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AsmAuthorizeAttribute(ModuleCode module, AccessType access, params AccessType[] orAccess)
    : AuthorizeAttribute, IAuthorizationRequirementData
{
    private readonly AsmPermissionRequirement _requirement = new(
        [new AsmPermission(module, access), .. orAccess.Select(a => new AsmPermission(module, a))]);

    /// <summary>Module the permission applies to.</summary>
    public ModuleCode Module { get; } = module;

    /// <inheritdoc />
    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return _requirement;
    }
}
