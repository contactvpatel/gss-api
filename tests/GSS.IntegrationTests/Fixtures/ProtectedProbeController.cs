using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>
/// Test-only endpoint with no [AllowAnonymous] and no external calls, so authentication tests exercise the
/// SSO fallback policy without depending on a feature controller. Added as an application part by <see cref="WebAppFactory"/>.
/// </summary>
[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/test-probe")]
public sealed class ProtectedProbeController : ControllerBase
{
    public const string Path = "/api/v1/test-probe";

    [HttpGet]
    public IActionResult Get()
    {
        return Ok();
    }
}
