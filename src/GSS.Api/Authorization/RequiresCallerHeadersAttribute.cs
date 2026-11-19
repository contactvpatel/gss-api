namespace GSS.Api.Authorization;

/// <summary>
/// Marks an action that validates the caller's x-app-* headers itself (<see cref="ICallerValidator"/>), so the
/// OpenAPI document lists the headers and the 403. <see cref="AsmAuthorizeAttribute"/> implies it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresCallerHeadersAttribute : Attribute;
