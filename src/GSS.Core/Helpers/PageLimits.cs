namespace GSS.Core.Helpers;

/// <summary>
/// Page size limits shared by the API's pagination validator and the repositories.
/// </summary>
public static class PageLimits
{
    /// <summary>Minimum page size.</summary>
    public const int MinPageSize = 1;

    /// <summary>Maximum page size.</summary>
    public const int MaxPageSize = 100;
}
