namespace GSS.Api.Models;

/// <summary>
/// A configured MIS division a client can choose from.
/// </summary>
public sealed record DivisionResponse
{
    /// <summary>Division ID.</summary>
    public required int DivisionId { get; init; }

    /// <summary>Division display name.</summary>
    public required string DivisionName { get; init; }
}
