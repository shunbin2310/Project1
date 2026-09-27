using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.Inventory;
using Project1.Api.Entities;
using Project1.Api.Services.Inventory;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.InventoryReaders)]
[Route("api/inventory")]
public sealed class InventoryController(IInventoryService inventoryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InventoryBalanceResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InventoryBalanceResponse>>> GetAll(
        [FromQuery] int? productCategoryId = null,
        [FromQuery] bool? lowStock = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await inventoryService.GetBalancesAsync(
            productCategoryId,
            lowStock,
            includeInactive,
            search,
            cancellationToken));
    }

    [HttpGet("{productId:int}", Name = "GetInventoryByProductId")]
    [ProducesResponseType<InventoryBalanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryBalanceResponse>> GetByProductId(
        int productId,
        CancellationToken cancellationToken)
    {
        var balance = await inventoryService.GetBalanceAsync(productId, cancellationToken);
        return balance is null ? NotFound() : Ok(balance);
    }

    [HttpGet("{productId:int}/transactions")]
    [ProducesResponseType<IReadOnlyList<InventoryTransactionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<InventoryTransactionResponse>>> GetTransactions(
        int productId,
        [FromQuery] InventoryTransactionType? type = null,
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom.Value > dateTo.Value)
        {
            ModelState.AddModelError(nameof(dateTo), "Date to must be on or after date from.");
            return BadRequest(new ValidationProblemDetails(ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Inventory transaction date range is invalid."
            });
        }

        return Ok(await inventoryService.GetTransactionsAsync(
            productId,
            type,
            dateFrom,
            dateTo,
            cancellationToken));
    }
}
