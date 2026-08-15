using Microsoft.AspNetCore.Mvc;

namespace Project1.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            message = "Project1 API is running"
        });
    }
}