using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Options;

namespace XDS_GHC_Verification.Controllers;

[ApiController]
[Route("health")]
public class HealthController(IOptions<AppInfoOptions> appInfo, IWebHostEnvironment env) : ControllerBase
{
    /// <summary>Returns service status. Does not require an API key.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        service = appInfo.Value.Name,
        version = appInfo.Value.Version,
        environment = env.EnvironmentName,
    });
}
