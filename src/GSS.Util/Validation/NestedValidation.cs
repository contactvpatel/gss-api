using System.ComponentModel.DataAnnotations;

namespace GSS.Util.Validation;

/// <summary>
/// DataAnnotations (and so <c>ValidateDataAnnotations()</c>) never look inside nested objects. Options classes call
/// this from <see cref="IValidatableObject.Validate"/> so a nested object's attributes run too.
/// </summary>
public static class NestedValidation
{
    /// <summary>Validates <paramref name="value"/>'s properties; failures name <c>Member.Property</c>.</summary>
    public static IEnumerable<ValidationResult> Validate(object? value, string memberName)
    {
        if (value is null)
        {
            return [];
        }

        List<ValidationResult> results = [];
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results.Select(r => new ValidationResult(r.ErrorMessage, [.. r.MemberNames.Select(m => $"{memberName}.{m}")]));
    }
}
