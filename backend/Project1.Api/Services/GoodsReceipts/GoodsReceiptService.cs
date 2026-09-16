using System.Data;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.GoodsReceipts;
using Project1.Api.Entities;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Services.GoodsReceipts;

public sealed class GoodsReceiptService(
    AppDbContext dbContext,
    ICurrentUserContext currentUser) : IGoodsReceiptService
{
    public async Task<IReadOnlyList<GoodsReceiptResponse>> GetAllAsync(
        int? purchaseOrderId,
        int? supplierId,
        GoodsReceiptStatus? status,
        CancellationToken cancellationToken)
    {
        var query = ReadQuery();

        if (purchaseOrderId.HasValue)
        {
            query = query.Where(receipt => receipt.PurchaseOrderId == purchaseOrderId.Value);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(receipt => receipt.SupplierId == supplierId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(receipt => receipt.Status == status.Value);
        }

        var receipts = await query
            .OrderByDescending(receipt => receipt.Id)
            .ToListAsync(cancellationToken);

        return receipts.Select(ToResponse).ToList();
    }

    public async Task<GoodsReceiptResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var receipt = await ReadQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return receipt is null ? null : ToResponse(receipt);
    }

    public async Task<GoodsReceiptOperationResult> CreateAsync(
        CreateGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateValues(
            request.ReceivedDate,
            request.SupplierDeliveryNoteNumber,
            request.Notes,
            request.Items);
        if (request.PurchaseOrderId < 1)
        {
            return ValidationFailed("Select a purchase order.");
        }

        if (validationError is not null)
        {
            return ValidationFailed(validationError);
        }

        var purchaseOrder = await LoadPurchaseOrderAsync(
            request.PurchaseOrderId,
            cancellationToken);
        if (purchaseOrder is null)
        {
            return NotFound("The selected purchase order does not exist.");
        }

        var stateError = ValidatePurchaseOrderState(purchaseOrder);
        if (stateError is not null)
        {
            return InvalidState(stateError);
        }

        if (request.ReceivedDate < purchaseOrder.OrderDate)
        {
            return ValidationFailed("Received date cannot be before the purchase order date.");
        }

        var existingDraft = await dbContext.GoodsReceipts.AnyAsync(
            receipt => receipt.PurchaseOrderId == purchaseOrder.Id &&
                       receipt.Status == GoodsReceiptStatus.Draft,
            cancellationToken);
        if (existingDraft)
        {
            return new GoodsReceiptOperationResult(
                GoodsReceiptOperationStatus.DuplicateDraft,
                ErrorMessage: "Complete or delete the existing draft receipt first.");
        }

        var deliveryNote = NormalizeOptionalText(request.SupplierDeliveryNoteNumber);
        if (await DeliveryNoteExistsAsync(
                purchaseOrder.Id,
                deliveryNote,
                excludedReceiptId: null,
                cancellationToken))
        {
            return DuplicateDeliveryNote();
        }

        var postedQuantities = await GetPostedQuantitiesAsync(
            purchaseOrder.Id,
            cancellationToken);
        var itemValidation = ValidateAndMapItems(
            purchaseOrder,
            request.Items,
            postedQuantities);
        if (itemValidation.Error is not null)
        {
            return itemValidation.Error;
        }

        var receipt = new GoodsReceipt
        {
            GoodsReceiptNumber = CreateTemporaryNumber(),
            PurchaseOrder = purchaseOrder,
            Supplier = purchaseOrder.Supplier,
            PurchaseOrderNumber = purchaseOrder.PurchaseOrderNumber,
            SupplierCode = purchaseOrder.SupplierCode,
            SupplierName = purchaseOrder.SupplierName,
            SupplierDeliveryNoteNumber = deliveryNote,
            ReceivedDate = request.ReceivedDate,
            Notes = NormalizeOptionalText(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedByName = CurrentUserName(),
            Items = itemValidation.Items!
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        dbContext.GoodsReceipts.Add(receipt);
        await dbContext.SaveChangesAsync(cancellationToken);

        receipt.GoodsReceiptNumber = $"GRN-{receipt.Id:D4}";
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await SuccessResultAsync(receipt.Id, cancellationToken);
    }

    public async Task<GoodsReceiptOperationResult> UpdateAsync(
        int id,
        UpdateGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateValues(
            request.ReceivedDate,
            request.SupplierDeliveryNoteNumber,
            request.Notes,
            request.Items);
        if (validationError is not null)
        {
            return ValidationFailed(validationError);
        }

        var receipt = await dbContext.GoodsReceipts
            .AsSplitQuery()
            .Include(item => item.Items)
            .Include(item => item.PurchaseOrder)
                .ThenInclude(order => order.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (receipt is null)
        {
            return NotFound();
        }

        if (receipt.Status != GoodsReceiptStatus.Draft)
        {
            return InvalidState("Only draft goods receipts can be edited.");
        }

        var stateError = ValidatePurchaseOrderState(receipt.PurchaseOrder);
        if (stateError is not null)
        {
            return InvalidState(stateError);
        }

        if (request.ReceivedDate < receipt.PurchaseOrder.OrderDate)
        {
            return ValidationFailed("Received date cannot be before the purchase order date.");
        }

        var deliveryNote = NormalizeOptionalText(request.SupplierDeliveryNoteNumber);
        if (await DeliveryNoteExistsAsync(
                receipt.PurchaseOrderId,
                deliveryNote,
                receipt.Id,
                cancellationToken))
        {
            return DuplicateDeliveryNote();
        }

        var postedQuantities = await GetPostedQuantitiesAsync(
            receipt.PurchaseOrderId,
            cancellationToken);
        var itemValidation = ValidateAndMapItems(
            receipt.PurchaseOrder,
            request.Items,
            postedQuantities);
        if (itemValidation.Error is not null)
        {
            return itemValidation.Error;
        }

        dbContext.GoodsReceiptItems.RemoveRange(receipt.Items);
        receipt.Items = itemValidation.Items!;
        receipt.SupplierDeliveryNoteNumber = deliveryNote;
        receipt.ReceivedDate = request.ReceivedDate;
        receipt.Notes = NormalizeOptionalText(request.Notes);
        receipt.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<GoodsReceiptOperationResult> PostAsync(
        int id,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var receipt = await dbContext.GoodsReceipts
            .AsSplitQuery()
            .Include(item => item.Items)
            .Include(item => item.PurchaseOrder)
                .ThenInclude(order => order.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (receipt is null)
        {
            return NotFound();
        }

        if (receipt.Status != GoodsReceiptStatus.Draft)
        {
            return InvalidState("Only draft goods receipts can be posted.");
        }

        var stateError = ValidatePurchaseOrderState(receipt.PurchaseOrder);
        if (stateError is not null)
        {
            return InvalidState(stateError);
        }

        var postedQuantities = await GetPostedQuantitiesAsync(
            receipt.PurchaseOrderId,
            cancellationToken);
        foreach (var item in receipt.Items)
        {
            var alreadyPosted = postedQuantities.GetValueOrDefault(item.PurchaseOrderItemId);
            if (alreadyPosted + item.QuantityReceived > item.OrderedQuantity)
            {
                return QuantityExceeded(
                    $"Received quantity for {item.ProductCode} exceeds the remaining order quantity.");
            }

            postedQuantities[item.PurchaseOrderItemId] = alreadyPosted + item.QuantityReceived;
        }

        var fullyReceived = receipt.PurchaseOrder.Items.All(
            item => postedQuantities.GetValueOrDefault(item.Id) >= item.Quantity);
        var now = DateTimeOffset.UtcNow;

        receipt.Status = GoodsReceiptStatus.Posted;
        receipt.PostedAtUtc = now;
        receipt.PostedByUserId = currentUser.UserId;
        receipt.PostedByName = CurrentUserName();
        receipt.UpdatedAtUtc = now;
        receipt.PurchaseOrder.Status = fullyReceived
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
        receipt.PurchaseOrder.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<GoodsReceiptOperationResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.GoodsReceipts
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (receipt is null)
        {
            return NotFound();
        }

        if (receipt.Status != GoodsReceiptStatus.Draft)
        {
            return InvalidState("Only draft goods receipts can be deleted.");
        }

        dbContext.GoodsReceipts.Remove(receipt);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new GoodsReceiptOperationResult(GoodsReceiptOperationStatus.Success);
    }

    private IQueryable<GoodsReceipt> ReadQuery() =>
        dbContext.GoodsReceipts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(receipt => receipt.Items);

    private Task<PurchaseOrder?> LoadPurchaseOrderAsync(
        int id,
        CancellationToken cancellationToken) =>
        dbContext.PurchaseOrders
            .AsSplitQuery()
            .Include(order => order.Supplier)
            .Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

    private async Task<Dictionary<int, decimal>> GetPostedQuantitiesAsync(
        int purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var postedItems = await dbContext.GoodsReceiptItems
            .Where(item => item.GoodsReceipt.PurchaseOrderId == purchaseOrderId &&
                           item.GoodsReceipt.Status == GoodsReceiptStatus.Posted)
            .Select(item => new
            {
                item.PurchaseOrderItemId,
                item.QuantityReceived
            })
            .ToListAsync(cancellationToken);

        return postedItems
            .GroupBy(item => item.PurchaseOrderItemId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.QuantityReceived));
    }

    private async Task<bool> DeliveryNoteExistsAsync(
        int purchaseOrderId,
        string? deliveryNote,
        int? excludedReceiptId,
        CancellationToken cancellationToken)
    {
        if (deliveryNote is null)
        {
            return false;
        }

        return await dbContext.GoodsReceipts.AnyAsync(
            receipt => receipt.PurchaseOrderId == purchaseOrderId &&
                       receipt.SupplierDeliveryNoteNumber == deliveryNote &&
                       (!excludedReceiptId.HasValue || receipt.Id != excludedReceiptId.Value),
            cancellationToken);
    }

    private static ItemValidationResult ValidateAndMapItems(
        PurchaseOrder purchaseOrder,
        IReadOnlyList<GoodsReceiptItemInput> inputs,
        IReadOnlyDictionary<int, decimal> postedQuantities)
    {
        var orderItems = purchaseOrder.Items.ToDictionary(item => item.Id);
        var receiptItems = new List<GoodsReceiptItem>(inputs.Count);

        foreach (var input in inputs)
        {
            if (!orderItems.TryGetValue(input.PurchaseOrderItemId, out var orderItem))
            {
                return new ItemValidationResult(
                    Error: ValidationFailed(
                        "Every receipt item must belong to the selected purchase order."));
            }

            var remainingQuantity = orderItem.Quantity -
                                    postedQuantities.GetValueOrDefault(orderItem.Id);
            if (input.QuantityReceived > remainingQuantity)
            {
                return new ItemValidationResult(
                    Error: QuantityExceeded(
                        $"Received quantity for {orderItem.ProductCode} exceeds the remaining order quantity of {remainingQuantity}."));
            }

            receiptItems.Add(new GoodsReceiptItem
            {
                PurchaseOrderItem = orderItem,
                ProductId = orderItem.ProductId,
                ProductCode = orderItem.ProductCode,
                ProductName = orderItem.ProductName,
                UnitOfMeasureCode = orderItem.UnitOfMeasureCode,
                OrderedQuantity = orderItem.Quantity,
                QuantityReceived = input.QuantityReceived
            });
        }

        return new ItemValidationResult(receiptItems);
    }

    private async Task<GoodsReceiptOperationResult> SuccessResultAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var receipt = await GetByIdAsync(id, cancellationToken);
        return new GoodsReceiptOperationResult(
            GoodsReceiptOperationStatus.Success,
            receipt);
    }

    private static GoodsReceiptResponse ToResponse(GoodsReceipt receipt)
    {
        var items = receipt.Items
            .OrderBy(item => item.Id)
            .Select(item => new GoodsReceiptItemResponse(
                item.Id,
                item.PurchaseOrderItemId,
                item.ProductId,
                item.ProductCode,
                item.ProductName,
                item.UnitOfMeasureCode,
                item.OrderedQuantity,
                item.QuantityReceived))
            .ToList();

        return new GoodsReceiptResponse(
            receipt.Id,
            receipt.GoodsReceiptNumber,
            receipt.PurchaseOrderId,
            receipt.PurchaseOrderNumber,
            receipt.SupplierId,
            receipt.SupplierCode,
            receipt.SupplierName,
            receipt.SupplierDeliveryNoteNumber,
            receipt.ReceivedDate,
            receipt.Notes,
            receipt.Status,
            receipt.CreatedByUserId,
            receipt.CreatedByName,
            receipt.CreatedAtUtc,
            receipt.UpdatedAtUtc,
            receipt.PostedAtUtc,
            receipt.PostedByUserId,
            receipt.PostedByName,
            items);
    }

    private static string? ValidateValues(
        DateOnly receivedDate,
        string? deliveryNote,
        string? notes,
        IReadOnlyList<GoodsReceiptItemInput> items)
    {
        if (receivedDate == default)
        {
            return "Received date is required.";
        }

        if (receivedDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return "Received date cannot be in the future.";
        }

        if ((deliveryNote?.Length ?? 0) > 100)
        {
            return "Supplier delivery note number cannot exceed 100 characters.";
        }

        if ((notes?.Length ?? 0) > 1000)
        {
            return "Notes cannot exceed 1000 characters.";
        }

        if (items.Count == 0)
        {
            return "Enter at least one received item.";
        }

        if (items.Any(item => item.PurchaseOrderItemId < 1 || item.QuantityReceived <= 0))
        {
            return "Every receipt item must have a valid purchase order item and a quantity greater than zero.";
        }

        if (items.Select(item => item.PurchaseOrderItemId).Distinct().Count() != items.Count)
        {
            return "A purchase order item cannot appear more than once in the same receipt.";
        }

        return null;
    }

    private static string? ValidatePurchaseOrderState(PurchaseOrder purchaseOrder) =>
        purchaseOrder.Status is PurchaseOrderStatus.Issued or PurchaseOrderStatus.PartiallyReceived
            ? null
            : "Goods can only be received against an issued or partially received purchase order.";

    private string CurrentUserName() =>
        string.IsNullOrWhiteSpace(currentUser.DisplayName)
            ? "Unknown user"
            : currentUser.DisplayName.Trim();

    private static string CreateTemporaryNumber() => $"TMP-{Guid.NewGuid():N}"[..20];

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static GoodsReceiptOperationResult NotFound(string? message = null) =>
        new(GoodsReceiptOperationStatus.NotFound, ErrorMessage: message);

    private static GoodsReceiptOperationResult ValidationFailed(string message) =>
        new(GoodsReceiptOperationStatus.ValidationFailed, ErrorMessage: message);

    private static GoodsReceiptOperationResult InvalidState(string message) =>
        new(GoodsReceiptOperationStatus.InvalidState, ErrorMessage: message);

    private static GoodsReceiptOperationResult DuplicateDeliveryNote() =>
        new(
            GoodsReceiptOperationStatus.DuplicateDeliveryNote,
            ErrorMessage: "This supplier delivery note has already been recorded for the purchase order.");

    private static GoodsReceiptOperationResult QuantityExceeded(string message) =>
        new(GoodsReceiptOperationStatus.QuantityExceeded, ErrorMessage: message);

    private sealed record ItemValidationResult(
        List<GoodsReceiptItem>? Items = null,
        GoodsReceiptOperationResult? Error = null);
}
