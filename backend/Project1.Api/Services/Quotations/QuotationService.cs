using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Quotations;
using Project1.Api.Entities;
using Project1.Api.Entities.Workflows;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Services.Quotations;

public sealed class QuotationService(
    AppDbContext dbContext,
    ICurrentUserContext currentUser) : IQuotationService
{
    private const string PurchaseRequestEntityType = "PurchaseRequest";
    private const string ApprovedStepCode = "APPROVED";

    public async Task<IReadOnlyList<QuotationResponse>> GetAllAsync(
        int? purchaseRequestId,
        QuotationStatus? status,
        CancellationToken cancellationToken)
    {
        var query = ReadQuery();

        if (purchaseRequestId.HasValue)
        {
            query = query.Where(item => item.PurchaseRequestId == purchaseRequestId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var quotations = await query
            .OrderByDescending(item => item.Id)
            .ToListAsync(cancellationToken);

        return quotations.Select(ToResponse).ToList();
    }

    public async Task<QuotationResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quotation = await ReadQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return quotation is null ? null : ToResponse(quotation);
    }

    public async Task<QuotationComparisonResponse?> GetComparisonAsync(
        int purchaseRequestId,
        CancellationToken cancellationToken)
    {
        var purchaseRequest = await dbContext.PurchaseRequests
            .AsNoTracking()
            .Where(item => item.Id == purchaseRequestId)
            .Select(item => new { item.Id, item.RequestNumber })
            .SingleOrDefaultAsync(cancellationToken);

        if (purchaseRequest is null)
        {
            return null;
        }

        var quotations = await ReadQuery()
            .Where(item =>
                item.PurchaseRequestId == purchaseRequestId &&
                item.Status != QuotationStatus.Draft)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var responses = quotations.Select(ToResponse).ToList();
        var lowestTotal = responses.Count == 0
            ? (decimal?)null
            : responses.Min(item => item.TotalAmount);

        var comparisonEntries = responses.Select(item => new QuotationComparisonEntryResponse(
            item.Id,
            item.QuotationNumber,
            item.SupplierId,
            item.SupplierCode,
            item.SupplierName,
            item.SupplierQuotationReference,
            item.QuotationDate,
            item.ValidUntil,
            item.Status,
            item.TotalAmount,
            lowestTotal.HasValue && item.TotalAmount == lowestTotal.Value,
            item.Status == QuotationStatus.Selected,
            item.Items)).ToList();

        return new QuotationComparisonResponse(
            purchaseRequest.Id,
            purchaseRequest.RequestNumber,
            responses
                .Where(item => item.Status == QuotationStatus.Selected)
                .Select(item => (int?)item.Id)
                .SingleOrDefault(),
            lowestTotal,
            comparisonEntries);
    }

    public async Task<QuotationOperationResult> CreateAsync(
        CreateQuotationRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateDraftValues(
            request.QuotationDate,
            request.ValidUntil,
            request.Items);
        if (validationError is not null)
        {
            return ValidationFailed(validationError);
        }

        var purchaseRequest = await dbContext.PurchaseRequests
            .AsSplitQuery()
            .Include(item => item.Items)
                .ThenInclude(item => item.Product)
                    .ThenInclude(product => product.UnitOfMeasure)
            .SingleOrDefaultAsync(item => item.Id == request.PurchaseRequestId, cancellationToken);

        if (purchaseRequest is null)
        {
            return NotFound("The selected purchase request does not exist.");
        }

        if (!await IsPurchaseRequestApprovedAsync(purchaseRequest.Id, cancellationToken))
        {
            return InvalidState("Only approved purchase requests can receive quotations.");
        }

        if (await HasSelectedQuotationAsync(purchaseRequest.Id, cancellationToken))
        {
            return InvalidState("A quotation has already been selected for this purchase request.");
        }

        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(
            item => item.Id == request.SupplierId && item.IsActive,
            cancellationToken);

        if (supplier is null)
        {
            return SupplierUnavailable();
        }

        var duplicate = await dbContext.Quotations.AnyAsync(
            item => item.PurchaseRequestId == purchaseRequest.Id &&
                    item.SupplierId == supplier.Id,
            cancellationToken);

        if (duplicate)
        {
            return new QuotationOperationResult(
                QuotationOperationStatus.DuplicateQuotation,
                ErrorMessage: "This supplier already has a quotation for the purchase request.");
        }

        var itemValidation = ValidateRequestedItems(purchaseRequest.Items, request.Items);
        if (itemValidation is not null)
        {
            return itemValidation;
        }

        var eligibilityError = await ValidateSupplierEligibilityAsync(
            supplier.Id,
            purchaseRequest.Items.Select(item => item.ProductId),
            cancellationToken);
        if (eligibilityError is not null)
        {
            return eligibilityError;
        }

        var requestedPrices = request.Items.ToDictionary(
            item => item.PurchaseRequestItemId,
            item => item.UnitPrice);
        var quotation = new Quotation
        {
            QuotationNumber = CreateTemporaryNumber(),
            PurchaseRequest = purchaseRequest,
            Supplier = supplier,
            SupplierCode = supplier.Code,
            SupplierName = supplier.Name,
            SupplierQuotationReference = NormalizeOptionalText(request.SupplierQuotationReference),
            QuotationDate = request.QuotationDate,
            ValidUntil = request.ValidUntil,
            Notes = NormalizeOptionalText(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedByName = CurrentUserName(),
            Items = purchaseRequest.Items
                .OrderBy(item => item.Id)
                .Select(item => new QuotationItem
                {
                    PurchaseRequestItemId = item.Id,
                    ProductId = item.ProductId,
                    ProductCode = item.Product.Code,
                    ProductName = item.Product.Name,
                    UnitOfMeasureCode = item.Product.UnitOfMeasure.Code,
                    Quantity = item.Quantity,
                    UnitPrice = requestedPrices[item.Id]
                })
                .ToList()
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Quotations.Add(quotation);
        await dbContext.SaveChangesAsync(cancellationToken);

        quotation.QuotationNumber = $"QT-{quotation.Id:D4}";
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await SuccessResultAsync(quotation.Id, cancellationToken);
    }

    public async Task<QuotationOperationResult> UpdateAsync(
        int id,
        UpdateQuotationRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateDraftValues(
            request.QuotationDate,
            request.ValidUntil,
            request.Items);
        if (validationError is not null)
        {
            return ValidationFailed(validationError);
        }

        var quotation = await TrackedQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (quotation is null)
        {
            return NotFound();
        }

        if (quotation.Status != QuotationStatus.Draft)
        {
            return InvalidState("Only draft quotations can be edited.");
        }

        if (await HasSelectedQuotationAsync(quotation.PurchaseRequestId, cancellationToken))
        {
            return InvalidState("A quotation has already been selected for this purchase request.");
        }

        var itemValidation = ValidateRequestedItems(
            quotation.Items.Select(item => item.PurchaseRequestItem),
            request.Items);
        if (itemValidation is not null)
        {
            return itemValidation;
        }

        if (!quotation.Supplier.IsActive)
        {
            return SupplierUnavailable();
        }

        var eligibilityError = await ValidateSupplierEligibilityAsync(
            quotation.SupplierId,
            quotation.Items.Select(item => item.ProductId),
            cancellationToken);
        if (eligibilityError is not null)
        {
            return eligibilityError;
        }

        var requestedPrices = request.Items.ToDictionary(
            item => item.PurchaseRequestItemId,
            item => item.UnitPrice);

        quotation.SupplierQuotationReference = NormalizeOptionalText(
            request.SupplierQuotationReference);
        quotation.QuotationDate = request.QuotationDate;
        quotation.ValidUntil = request.ValidUntil;
        quotation.Notes = NormalizeOptionalText(request.Notes);
        quotation.UpdatedAtUtc = DateTimeOffset.UtcNow;

        foreach (var item in quotation.Items)
        {
            item.UnitPrice = requestedPrices[item.PurchaseRequestItemId];
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<QuotationOperationResult> SubmitAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quotation = await TrackedQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (quotation is null)
        {
            return NotFound();
        }

        if (quotation.Status != QuotationStatus.Draft)
        {
            return InvalidState("Only draft quotations can be submitted.");
        }

        if (await HasSelectedQuotationAsync(quotation.PurchaseRequestId, cancellationToken))
        {
            return InvalidState("A quotation has already been selected for this purchase request.");
        }

        if (!quotation.Supplier.IsActive)
        {
            return SupplierUnavailable();
        }

        var eligibilityError = await ValidateSupplierEligibilityAsync(
            quotation.SupplierId,
            quotation.Items.Select(item => item.ProductId),
            cancellationToken);
        if (eligibilityError is not null)
        {
            return eligibilityError;
        }

        if (quotation.Items.Count == 0 || quotation.Items.Any(item => item.UnitPrice <= 0))
        {
            return ValidationFailed("Every quotation item must have a unit price greater than zero.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (quotation.ValidUntil.HasValue && quotation.ValidUntil.Value < today)
        {
            return ValidationFailed("The quotation validity date has expired.");
        }

        quotation.Status = QuotationStatus.Submitted;
        quotation.SubmittedAtUtc = DateTimeOffset.UtcNow;
        quotation.UpdatedAtUtc = quotation.SubmittedAtUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<QuotationOperationResult> SelectAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quotation = await TrackedQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (quotation is null)
        {
            return NotFound();
        }

        if (quotation.Status != QuotationStatus.Submitted)
        {
            return InvalidState("Only submitted quotations can be selected.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (quotation.ValidUntil.HasValue && quotation.ValidUntil.Value < today)
        {
            return InvalidState("An expired quotation cannot be selected.");
        }

        if (await HasSelectedQuotationAsync(quotation.PurchaseRequestId, cancellationToken))
        {
            return InvalidState("A quotation has already been selected for this purchase request.");
        }

        var otherQuotations = await dbContext.Quotations
            .Where(item =>
                item.PurchaseRequestId == quotation.PurchaseRequestId &&
                item.Id != quotation.Id &&
                item.Status == QuotationStatus.Submitted)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        quotation.Status = QuotationStatus.Selected;
        quotation.SelectedAtUtc = now;
        quotation.UpdatedAtUtc = now;

        foreach (var otherQuotation in otherQuotations)
        {
            otherQuotation.Status = QuotationStatus.NotSelected;
            otherQuotation.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<QuotationOperationResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quotation = await dbContext.Quotations.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);

        if (quotation is null)
        {
            return NotFound();
        }

        if (quotation.Status != QuotationStatus.Draft)
        {
            return InvalidState("Only draft quotations can be deleted.");
        }

        dbContext.Quotations.Remove(quotation);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new QuotationOperationResult(QuotationOperationStatus.Success);
    }

    private IQueryable<Quotation> ReadQuery() =>
        dbContext.Quotations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.PurchaseRequest)
            .Include(item => item.Supplier)
            .Include(item => item.Items);

    private IQueryable<Quotation> TrackedQuery() =>
        dbContext.Quotations
            .AsSplitQuery()
            .Include(item => item.PurchaseRequest)
            .Include(item => item.Supplier)
            .Include(item => item.Items)
                .ThenInclude(item => item.PurchaseRequestItem);

    private async Task<bool> IsPurchaseRequestApprovedAsync(
        int purchaseRequestId,
        CancellationToken cancellationToken)
    {
        return await (
            from instance in dbContext.WorkflowProcessInstances.AsNoTracking()
            join step in dbContext.WorkflowStepInstances.AsNoTracking()
                on instance.CurrentStepInstanceId equals step.Id
            where instance.EntityType == PurchaseRequestEntityType &&
                  instance.EntityId == purchaseRequestId &&
                  instance.Status == WorkflowInstanceStatus.Completed &&
                  step.Code == ApprovedStepCode
            select instance.Id)
            .AnyAsync(cancellationToken);
    }

    private async Task<bool> HasSelectedQuotationAsync(
        int purchaseRequestId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Quotations.AnyAsync(
            item => item.PurchaseRequestId == purchaseRequestId &&
                    item.Status == QuotationStatus.Selected,
            cancellationToken);
    }

    private async Task<QuotationOperationResult?> ValidateSupplierEligibilityAsync(
        int supplierId,
        IEnumerable<int> productIds,
        CancellationToken cancellationToken)
    {
        var requiredProductIds = productIds.Distinct().ToList();
        var suppliedProductIds = await dbContext.SupplierProducts
            .Where(item =>
                item.SupplierId == supplierId &&
                item.IsActive &&
                item.Supplier.IsActive &&
                item.Product.IsActive &&
                requiredProductIds.Contains(item.ProductId))
            .Select(item => item.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var missingProductIds = requiredProductIds.Except(suppliedProductIds).ToList();

        if (missingProductIds.Count == 0)
        {
            return null;
        }

        var missingProductCodes = await dbContext.Products
            .Where(item => missingProductIds.Contains(item.Id))
            .OrderBy(item => item.Code)
            .Select(item => item.Code)
            .ToListAsync(cancellationToken);

        return new QuotationOperationResult(
            QuotationOperationStatus.SupplierCannotSupplyProducts,
            ErrorMessage: missingProductCodes.Count == 0
                ? "The supplier cannot supply all products in this purchase request."
                : $"The supplier is not active for: {string.Join(", ", missingProductCodes)}.");
    }

    private static QuotationOperationResult? ValidateRequestedItems(
        IEnumerable<PurchaseRequestItem> purchaseRequestItems,
        IReadOnlyList<QuotationItemPriceRequest> requestedItems)
    {
        var expectedIds = purchaseRequestItems
            .Select(item => item.Id)
            .OrderBy(id => id)
            .ToList();
        var requestedIds = requestedItems
            .Select(item => item.PurchaseRequestItemId)
            .OrderBy(id => id)
            .ToList();

        return expectedIds.SequenceEqual(requestedIds)
            ? null
            : ValidationFailed(
                "Quotation items must match every item in the selected purchase request.");
    }

    private static string? ValidateDraftValues(
        DateOnly quotationDate,
        DateOnly? validUntil,
        IReadOnlyList<QuotationItemPriceRequest> items)
    {
        if (quotationDate == default)
        {
            return "Quotation date is required.";
        }

        if (validUntil.HasValue && validUntil.Value < quotationDate)
        {
            return "Valid until must be on or after the quotation date.";
        }

        if (items.Count == 0)
        {
            return "Add every purchase request item to the quotation.";
        }

        if (items.GroupBy(item => item.PurchaseRequestItemId).Any(group => group.Count() > 1))
        {
            return "A purchase request item can only appear once in a quotation.";
        }

        if (items.Any(item => item.PurchaseRequestItemId < 1 || item.UnitPrice < 0))
        {
            return "Quotation item values are invalid.";
        }

        return null;
    }

    private async Task<QuotationOperationResult> SuccessResultAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quotation = await GetByIdAsync(id, cancellationToken);
        return new QuotationOperationResult(
            QuotationOperationStatus.Success,
            quotation);
    }

    private static QuotationResponse ToResponse(Quotation quotation)
    {
        var items = quotation.Items
            .OrderBy(item => item.Id)
            .Select(item => new QuotationItemResponse(
                item.Id,
                item.PurchaseRequestItemId,
                item.ProductId,
                item.ProductCode,
                item.ProductName,
                item.UnitOfMeasureCode,
                item.Quantity,
                item.UnitPrice,
                item.Quantity * item.UnitPrice))
            .ToList();

        return new QuotationResponse(
            quotation.Id,
            quotation.QuotationNumber,
            quotation.PurchaseRequestId,
            quotation.PurchaseRequest.RequestNumber,
            quotation.SupplierId,
            quotation.SupplierCode,
            quotation.SupplierName,
            quotation.SupplierQuotationReference,
            quotation.QuotationDate,
            quotation.ValidUntil,
            quotation.Notes,
            quotation.Status,
            items.Sum(item => item.LineTotal),
            quotation.CreatedByUserId,
            quotation.CreatedByName,
            quotation.CreatedAtUtc,
            quotation.UpdatedAtUtc,
            quotation.SubmittedAtUtc,
            quotation.SelectedAtUtc,
            items);
    }

    private string CurrentUserName() =>
        string.IsNullOrWhiteSpace(currentUser.DisplayName)
            ? "Unknown user"
            : currentUser.DisplayName.Trim();

    private static string CreateTemporaryNumber() => $"TMP-{Guid.NewGuid():N}"[..20];

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static QuotationOperationResult NotFound(string? message = null) =>
        new(QuotationOperationStatus.NotFound, ErrorMessage: message);

    private static QuotationOperationResult ValidationFailed(string message) =>
        new(QuotationOperationStatus.ValidationFailed, ErrorMessage: message);

    private static QuotationOperationResult InvalidState(string message) =>
        new(QuotationOperationStatus.InvalidState, ErrorMessage: message);

    private static QuotationOperationResult SupplierUnavailable() =>
        new(
            QuotationOperationStatus.SupplierUnavailable,
            ErrorMessage: "Select an active supplier.");
}
