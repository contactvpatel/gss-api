namespace GSS.Util.Security;

/// <summary><c>HttpContext.Items</c> keys shared by the authentication handler (Api) and <c>IUserContext</c> (Infrastructure).</summary>
public static class HttpContextItemKeys
{
    /// <summary>The caller's SSO token, stored after SSO validation. Never a claim.</summary>
    public const string UserToken = "UserToken";
}
