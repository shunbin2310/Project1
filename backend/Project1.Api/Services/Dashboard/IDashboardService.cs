using Project1.Api.DTOs.Dashboard;

namespace Project1.Api.Services.Dashboard;

public interface IDashboardService
{
    Task<DashboardResponse> GetAsync(CancellationToken cancellationToken);
}
