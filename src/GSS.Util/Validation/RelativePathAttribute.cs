using System.ComponentModel.DataAnnotations;

namespace GSS.Util.Validation;

/// <summary>
/// An endpoint path relative to the client's base URL. A leading slash would drop the base URL's path (every call
/// 404s) and an absolute URL would bypass it (and send credentials elsewhere). Blank passes; pair with [Required].
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RelativePathAttribute : ValidationAttribute
{
    /// <summary>Default message.</summary>
    public RelativePathAttribute()
        : base("{0} must be a path relative to the service URL, with no leading slash (e.g. \"user/logout\").")
    {
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value is not string path
            || path.Length == 0
            || (path[0] is not ('/' or '\\') && Uri.TryCreate(path, UriKind.Relative, out _));
    }
}
