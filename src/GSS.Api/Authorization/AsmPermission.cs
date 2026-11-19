using System.ComponentModel;

namespace GSS.Api.Authorization;

/// <summary>ASM module codes for this application; descriptions must match ASM.</summary>
public enum ModuleCode
{
    [Description("APP")] Application,
    [Description("ADMIN")] Administration
}

/// <summary>ASM access flags.</summary>
public enum AccessType
{
    View,
    Create,
    Update,
    Delete,
    Access,

    /// <summary>Any module access on the selected position.</summary>
    AllowAny
}

/// <summary>One module permission.</summary>
public readonly record struct AsmPermission(ModuleCode Module, AccessType Access);
