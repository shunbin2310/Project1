using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.Dashboard;
using Project1.Api.Services.Dashboard;

namespace Project1.Api.Tests.Controllers;

public sealed class DashboardControllerTests
{
    [Fact]
    public async Task Get_ReturnsRoleFilteredDashboardFromService()
    {
        var expected = new DashboardResponse(
            [new DashboardSummaryCardResponse(
                "my-drafts", "My drafts", 2, "Draft requests.", "/my-tasks", "neutral")],
            [],
            [],
            DateTimeOffset.UtcNow);
        var controller = new DashboardController(new FakeDashboardService(expected));

        var response = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, ok.Value);
    }

    private sealed class FakeDashboardService(DashboardResponse response) : IDashboardService
    {
        public Task<DashboardResponse> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
