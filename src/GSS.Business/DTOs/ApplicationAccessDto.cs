namespace GSS.Business.DTOs;

/// <summary>
/// One application or area and what the user can do in it: view, create, update, delete.
/// </summary>
public sealed record ApplicationAccessDto
{
    /// <summary>Module code (e.g. SHR).</summary>
    public required string ModuleCode { get; init; }

    /// <summary>Display name of the module.</summary>
    public required string ModuleName { get; init; }

    /// <summary>Type of the module (e.g. Module).</summary>
    public required string ModuleType { get; init; }

    /// <summary>Identifier of the module.</summary>
    public int ModuleId { get; init; }

    /// <summary>Identifier of the module type.</summary>
    public int ModuleTypeId { get; init; }

    /// <summary>Whether the user can view this module.</summary>
    public bool? HasViewAccess { get; init; }

    /// <summary>Whether the user can create in this module.</summary>
    public bool? HasCreateAccess { get; init; }

    /// <summary>Whether the user can update in this module.</summary>
    public bool? HasUpdateAccess { get; init; }

    /// <summary>Whether the user can delete in this module.</summary>
    public bool? HasDeleteAccess { get; init; }

    /// <summary>Whether the user has general access to this module.</summary>
    public bool? HasAccess { get; init; }
}
