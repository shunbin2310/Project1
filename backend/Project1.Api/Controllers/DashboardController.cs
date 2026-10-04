using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.DTOs.Dashboard;
using Project1.Api.Services.Dashboard;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardResponse>> Get(
        CancellationToken cancellationToken) =>
        Ok(await dashboardService.GetAsync(cancellationToken));
}
