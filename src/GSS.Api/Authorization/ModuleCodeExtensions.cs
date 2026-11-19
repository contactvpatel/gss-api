using System.Collections.Frozen;
using System.ComponentModel;
using System.Reflection;

namespace GSS.Api.Authorization;

/// <summary>ASM codes of <see cref="ModuleCode"/>, read once from their <c>[Description]</c>.</summary>
public static class ModuleCodeExtensions
{
    private static readonly FrozenDictionary<ModuleCode, string> Codes = Enum.GetValues<ModuleCode>().ToFrozenDictionary(
        module => module,
        module => typeof(ModuleCode).GetField(module.ToString())!.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? throw new InvalidOperationException($"ModuleCode.{module} has no [Description] with its ASM code."));

    /// <summary>The module's ASM code, e.g. <c>APP</c>.</summary>
    public static string ToAsmCode(this ModuleCode module)
    {
        return Codes[module];
    }
}
