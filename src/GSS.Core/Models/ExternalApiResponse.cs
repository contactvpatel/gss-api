namespace GSS.Core.Models;

/// <summary>
/// Response envelope shared by the MIS and ASM APIs: <c>{ succeeded, data[], message, errors[] }</c>.
/// </summary>
/// <typeparam name="T">The type of items in <see cref="Data"/>.</typeparam>
public class ExternalApiResponse<T>
{
    /// <summary>Whether the request succeeded.</summary>
    public bool Succeeded { get; set; }

    /// <summary>The payload when successful.</summary>
    public List<T>? Data { get; set; }

    /// <summary>Optional message from the API.</summary>
    public string? Message { get; set; }

    /// <summary>Errors when the request did not succeed.</summary>
    public List<ApiErrorModel>? Errors { get; set; }
}
