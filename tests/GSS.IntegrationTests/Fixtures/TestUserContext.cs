using GSS.Core.Interfaces.Base;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>A fixed caller for repositories built outside a request.</summary>
public sealed class TestUserContext(string? userId) : IUserContext
{
    /// <summary>User ID repository tests write as.</summary>
    public const string DefaultUserId = "1";

    public string? GetUserId() => userId;

    public string? GetToken() => null;

    public DateTimeOffset? GetTokenExpiresAtUtc() => null;
}
