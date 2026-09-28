using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using Project1.Api.Data;
using Project1.Api.DTOs.PurchaseOrders;
using Project1.Api.Email;
using Project1.Api.Entities;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Services.PurchaseOrders;

public sealed class PurchaseOrderService(
    AppDbContext dbContext,
    ICurrentUserContext currentUser,
    IPurchaseOrderEmailRenderer emailRenderer,
    IOptions<SmtpOptions> smtpOptions) : IPurchaseOrderService
{
    private readonly SmtpOptions smtp = smtpOptions.Value;

    public async Task<IReadOnlyList<PurchaseOrderResponse>> GetAllAsync(
        int? supplierId,
        PurchaseOrderStatus? status,
        CancellationToken cancellationToken)
    {
        var query = ReadQuery();

        if (supplierId.HasValue)
        {
            query = query.Where(order => order.SupplierId == supplierId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(order => order.Status == status.Value);
        }

        var purchaseOrders = await query
            .OrderByDescending(order => order.Id)
            .ToListAsync(cancellationToken);

        return purchaseOrders.Select(ToResponse).ToList();
    }

    public async Task<PurchaseOrderResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await ReadQuery()
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

        return purchaseOrder is null ? null : ToResponse(purchaseOrder);
    }

    public async Task<PurchaseOrderOperationResult> CreateAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateDraftValues(
            request.OrderDate,
            request.ExpectedDeliveryDate,
            request.DeliveryAddress,
            request.Notes);
        if (request.QuotationId < 1)
        {
            return ValidationFailed("Select a quotation.");
        }

        if (validationError is not null)
        {
            return ValidationFailed(validationError);
        }

        var quotation = await dbContext.Quotations
            .AsSplitQuery()
            .Include(item => item.PurchaseRequest)
            .Include(item => item.Supplier)
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == request.QuotationId, cancellationToken);

        if (quotation is null)
        {
            return NotFound("The selected quotation does not exist.");
        }

        if (quotation.Status != QuotationStatus.Selected)
        {
            return new PurchaseOrderOperationResult(
                PurchaseOrderOperationStatus.QuotationNotSelected,
                ErrorMessage: "Only a selected quotation can be converted into a purchase order.");
        }

        if (!quotation.Supplier.IsActive)
        {
            return SupplierUnavailable();
        }

        if (quotation.ValidUntil.HasValue &&
            quotation.ValidUntil.Value < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return InvalidState("The selected quotation has expired.");
        }

        if (quotation.Items.Count == 0 ||
            quotation.Items.Any(item => item.Quantity <= 0 || item.UnitPrice <= 0))
        {
            return InvalidState("The selected quotation does not contain valid order items.");
        }

        var duplicate = await dbContext.PurchaseOrders.AnyAsync(
            order => order.QuotationId == quotation.Id ||
                     order.PurchaseRequestId == quotation.PurchaseRequestId,
            cancellationToken);
        if (duplicate)
        {
            return new PurchaseOrderOperationResult(
                PurchaseOrderOperationStatus.DuplicatePurchaseOrder,
                ErrorMessage: "A purchase order already exists for this purchase request.");
        }

        var purchaseOrder = new PurchaseOrder
        {
            PurchaseOrderNumber = CreateTemporaryNumber(),
            Quotation = quotation,
            PurchaseRequest = quotation.PurchaseRequest,
            Supplier = quotation.Supplier,
            QuotationNumber = quotation.QuotationNumber,
            PurchaseRequestNumber = quotation.PurchaseRequest.RequestNumber,
            SupplierCode = quotation.SupplierCode,
            SupplierName = quotation.SupplierName,
            SupplierQuotationReference = quotation.SupplierQuotationReference,
            OrderDate = request.OrderDate,
            ExpectedDeliveryDate = request.ExpectedDeliveryDate,
            DeliveryAddress = NormalizeOptionalText(request.DeliveryAddress),
            Notes = NormalizeOptionalText(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedByName = CurrentUserName(),
            Items = quotation.Items
                .OrderBy(item => item.Id)
                .Select(item => new PurchaseOrderItem
                {
                    QuotationItem = item,
                    ProductId = item.ProductId,
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    UnitOfMeasureCode = item.UnitOfMeasureCode,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                })
                .ToList()
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.PurchaseOrders.Add(purchaseOrder);
        await dbContext.SaveChangesAsync(cancellationToken);

        purchaseOrder.PurchaseOrderNumber = $"PO-{purchaseOrder.Id:D4}";
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await SuccessResultAsync(purchaseOrder.Id, cancellationToken);
    }

    public async Task<PurchaseOrderOperationResult> UpdateAsync(
        int id,
        UpdatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateDraftValues(
            request.OrderDate,
            request.ExpectedDeliveryDate,
            request.DeliveryAddress,
            request.Notes);
        if (validationError is not null)
        {
            return ValidationFailed(validationError);
        }

        var purchaseOrder = await dbContext.PurchaseOrders
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (purchaseOrder is null)
        {
            return NotFound();
        }

        if (purchaseOrder.Status != PurchaseOrderStatus.Draft)
        {
            return InvalidState("Only draft purchase orders can be edited.");
        }

        purchaseOrder.OrderDate = request.OrderDate;
        purchaseOrder.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
        purchaseOrder.DeliveryAddress = NormalizeOptionalText(request.DeliveryAddress);
        purchaseOrder.Notes = NormalizeOptionalText(request.Notes);
        purchaseOrder.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderOperationResult> IssueAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.PurchaseOrders
            .Include(order => order.Supplier)
            .Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (purchaseOrder is null)
        {
            return NotFound();
        }

        if (purchaseOrder.Status != PurchaseOrderStatus.Draft)
        {
            return InvalidState("Only draft purchase orders can be issued.");
        }

        if (!purchaseOrder.Supplier.IsActive)
        {
            return SupplierUnavailable();
        }

        var supplierEmail = NormalizeOptionalText(purchaseOrder.Supplier.Email);
        if (supplierEmail is null || !MailAddress.TryCreate(supplierEmail, out _))
        {
            return ValidationFailed("The supplier must have a valid email address before issue.");
        }

        if (!purchaseOrder.ExpectedDeliveryDate.HasValue)
        {
            return ValidationFailed("Expected delivery date is required before issue.");
        }

        if (purchaseOrder.ExpectedDeliveryDate.Value < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return ValidationFailed("Expected delivery date cannot be in the past.");
        }

        if (string.IsNullOrWhiteSpace(purchaseOrder.DeliveryAddress))
        {
            return ValidationFailed("Delivery address is required before issue.");
        }

        if (purchaseOrder.Items.Count == 0 ||
            purchaseOrder.Items.Any(item => item.Quantity <= 0 || item.UnitPrice <= 0))
        {
            return InvalidState("The purchase order does not contain valid order items.");
        }

        var now = DateTimeOffset.UtcNow;
        purchaseOrder.Status = PurchaseOrderStatus.Issued;
        purchaseOrder.IssuedAtUtc = now;
        purchaseOrder.IssuedByUserId = currentUser.UserId;
        purchaseOrder.IssuedByName = CurrentUserName();
        purchaseOrder.UpdatedAtUtc = now;

        var email = emailRenderer.Render(purchaseOrder, supplierEmail);
        purchaseOrder.EmailOutboxes.Add(new EmailOutbox
        {
            SourceType = "PurchaseOrder",
            SourceId = purchaseOrder.Id,
            SourceReference = purchaseOrder.PurchaseOrderNumber,
            FromAddress = smtp.FromAddress,
            FromName = smtp.FromName,
            RecipientEmail = email.RecipientEmail,
            Subject = email.Subject,
            HtmlBody = email.HtmlBody,
            Status = EmailDeliveryStatus.Pending,
            CreatedByUserId = currentUser.UserId,
            CreatedByName = CurrentUserName(),
            CreatedDate = DateOnly.FromDateTime(now.UtcDateTime),
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderEmailPreviewResponse?> GetEmailPreviewAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var email = await dbContext.EmailOutboxes
            .AsNoTracking()
            .Where(item => item.PurchaseOrderId == id)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return email is null
            ? null
            : new PurchaseOrderEmailPreviewResponse(
                email.Id,
                email.RecipientEmail,
                email.Subject,
                email.HtmlBody,
                email.Status);
    }

    public async Task<PurchaseOrderOperationResult> RetryEmailAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.PurchaseOrders
            .Include(order => order.EmailOutboxes)
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (purchaseOrder is null)
        {
            return NotFound();
        }

        var email = purchaseOrder.EmailOutboxes
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();
        if (email is null)
        {
            return InvalidState("This purchase order does not have an email delivery record.");
        }

        if (email.Status != EmailDeliveryStatus.Failed)
        {
            return InvalidState("Only a failed email can be retried.");
        }

        email.Status = EmailDeliveryStatus.Pending;
        email.LastError = null;
        email.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderOperationResult> CancelAsync(
        int id,
        CancelPurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var reason = NormalizeOptionalText(request.Reason);
        if (reason is null)
        {
            return ValidationFailed("Cancellation reason is required.");
        }

        if (reason.Length > 500)
        {
            return ValidationFailed("Cancellation reason cannot exceed 500 characters.");
        }

        var purchaseOrder = await dbContext.PurchaseOrders
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (purchaseOrder is null)
        {
            return NotFound();
        }

        if (purchaseOrder.Status != PurchaseOrderStatus.Issued)
        {
            return InvalidState("Only issued purchase orders can be cancelled.");
        }

        var now = DateTimeOffset.UtcNow;
        purchaseOrder.Status = PurchaseOrderStatus.Cancelled;
        purchaseOrder.CancellationReason = reason;
        purchaseOrder.CancelledAtUtc = now;
        purchaseOrder.CancelledByUserId = currentUser.UserId;
        purchaseOrder.CancelledByName = CurrentUserName();
        purchaseOrder.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderOperationResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.PurchaseOrders
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (purchaseOrder is null)
        {
            return NotFound();
        }

        if (purchaseOrder.Status != PurchaseOrderStatus.Draft)
        {
            return InvalidState("Only draft purchase orders can be deleted.");
        }

        dbContext.PurchaseOrders.Remove(purchaseOrder);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PurchaseOrderOperationResult(PurchaseOrderOperationStatus.Success);
    }

    private IQueryable<PurchaseOrder> ReadQuery() =>
        dbContext.PurchaseOrders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(order => order.Items)
            .Include(order => order.EmailOutboxes);

    private async Task<PurchaseOrderOperationResult> SuccessResultAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await GetByIdAsync(id, cancellationToken);
        return new PurchaseOrderOperationResult(
            PurchaseOrderOperationStatus.Success,
            purchaseOrder);
    }

    private static PurchaseOrderResponse ToResponse(PurchaseOrder purchaseOrder)
    {
        var items = purchaseOrder.Items
            .OrderBy(item => item.Id)
            .Select(item => new PurchaseOrderItemResponse(
                item.Id,
                item.QuotationItemId,
                item.ProductId,
                item.ProductCode,
                item.ProductName,
                item.UnitOfMeasureCode,
                item.Quantity,
                item.UnitPrice,
                item.Quantity * item.UnitPrice))
            .ToList();

        var latestEmail = purchaseOrder.EmailOutboxes
            .OrderByDescending(email => email.Id)
            .FirstOrDefault();
        var emailDelivery = latestEmail is null
            ? null
            : new PurchaseOrderEmailDeliveryResponse(
                latestEmail.Id,
                latestEmail.RecipientEmail,
                latestEmail.Subject,
                latestEmail.Status,
                latestEmail.AttemptCount,
                latestEmail.CreatedAtUtc,
                latestEmail.LastAttemptAtUtc,
                latestEmail.SentAtUtc,
                latestEmail.LastError);

        return new PurchaseOrderResponse(
            purchaseOrder.Id,
            purchaseOrder.PurchaseOrderNumber,
            purchaseOrder.QuotationId,
            purchaseOrder.QuotationNumber,
            purchaseOrder.PurchaseRequestId,
            purchaseOrder.PurchaseRequestNumber,
            purchaseOrder.SupplierId,
            purchaseOrder.SupplierCode,
            purchaseOrder.SupplierName,
            purchaseOrder.SupplierQuotationReference,
            purchaseOrder.OrderDate,
            purchaseOrder.ExpectedDeliveryDate,
            purchaseOrder.DeliveryAddress,
            purchaseOrder.Notes,
            purchaseOrder.Status,
            items.Sum(item => item.LineTotal),
            purchaseOrder.CreatedByUserId,
            purchaseOrder.CreatedByName,
            purchaseOrder.CreatedAtUtc,
            purchaseOrder.UpdatedAtUtc,
            purchaseOrder.IssuedAtUtc,
            purchaseOrder.IssuedByUserId,
            purchaseOrder.IssuedByName,
            purchaseOrder.CancelledAtUtc,
            purchaseOrder.CancelledByUserId,
            purchaseOrder.CancelledByName,
            purchaseOrder.CancellationReason,
            emailDelivery,
            items);
    }

    private static string? ValidateDraftValues(
        DateOnly orderDate,
        DateOnly? expectedDeliveryDate,
        string? deliveryAddress,
        string? notes)
    {
        if (orderDate == default)
        {
            return "Order date is required.";
        }

        if (expectedDeliveryDate.HasValue && expectedDeliveryDate.Value < orderDate)
        {
            return "Expected delivery date must be on or after the order date.";
        }

        if ((deliveryAddress?.Length ?? 0) > 500)
        {
            return "Delivery address cannot exceed 500 characters.";
        }

        if ((notes?.Length ?? 0) > 1000)
        {
            return "Notes cannot exceed 1000 characters.";
        }

        return null;
    }

    private string CurrentUserName() =>
        string.IsNullOrWhiteSpace(currentUser.DisplayName)
            ? "Unknown user"
            : currentUser.DisplayName.Trim();

    private static string CreateTemporaryNumber() => $"TMP-{Guid.NewGuid():N}"[..20];

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PurchaseOrderOperationResult NotFound(string? message = null) =>
        new(PurchaseOrderOperationStatus.NotFound, ErrorMessage: message);

    private static PurchaseOrderOperationResult ValidationFailed(string message) =>
        new(PurchaseOrderOperationStatus.ValidationFailed, ErrorMessage: message);

    private static PurchaseOrderOperationResult InvalidState(string message) =>
        new(PurchaseOrderOperationStatus.InvalidState, ErrorMessage: message);

    private static PurchaseOrderOperationResult SupplierUnavailable() =>
        new(
            PurchaseOrderOperationStatus.SupplierUnavailable,
            ErrorMessage: "The selected supplier is inactive.");
}
