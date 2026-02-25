using Microsoft.AspNetCore.Mvc;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok("LMS API Running Healthy 🚀");
    }
}
