using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.Inventory;
using Project1.Api.Entities;
using Project1.Api.Services.Inventory;

namespace Project1.Api.Tests.Controllers;

public sealed class InventoryControllerTests
{
    [Fact]
    public async Task GetByProductId_ReturnsNotFound_WhenProductDoesNotExist()
    {
        var controller = new InventoryController(new FakeInventoryService());

        var response = await controller.GetByProductId(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Fact]
    public async Task GetTransactions_ReturnsValidationProblem_WhenDateRangeIsInvalid()
    {
        var controller = new InventoryController(new FakeInventoryService());

        var response = await controller.GetTransactions(
            1,
            type: null,
            dateFrom: new DateOnly(2026, 9, 20),
            dateTo: new DateOnly(2026, 9, 19),
            CancellationToken.None);

        var problem = Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal(400, problem.StatusCode);
    }

    private sealed class FakeInventoryService : IInventoryService
    {
        public Task<IReadOnlyList<InventoryBalanceResponse>> GetBalancesAsync(
            int? productCategoryId,
            bool? lowStock,
            bool includeInactive,
            string? search,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InventoryBalanceResponse>>([]);

        public Task<InventoryBalanceResponse?> GetBalanceAsync(
            int productId,
            CancellationToken cancellationToken) =>
            Task.FromResult<InventoryBalanceResponse?>(null);

        public Task<IReadOnlyList<InventoryTransactionResponse>> GetTransactionsAsync(
            int productId,
            InventoryTransactionType? type,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InventoryTransactionResponse>>([]);
    }
}
