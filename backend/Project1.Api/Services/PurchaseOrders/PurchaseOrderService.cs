using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using Project1.Api.Data;
using Project1.Api.DTOs.PurchaseOrders;
using Project1.Api.DTOs.Workflows;
using Project1.Api.Email;
using Project1.Api.Entities;
using Project1.Api.Services.Authentication;
using Project1.Api.Services.Workflows;

namespace Project1.Api.Services.PurchaseOrders;

public sealed class PurchaseOrderService(
    AppDbContext dbContext,
    IWorkflowEngine workflowEngine,
    ICurrentUserContext currentUser,
    IEmailTemplateRenderer emailRenderer,
    IPurchaseOrderPdfGenerator pdfGenerator,
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
        var workflows = await workflowEngine.GetInstancesAsync(
            PurchaseOrderWorkflow.EntityType,
            purchaseOrders.Select(order => order.Id).ToList(),
            cancellationToken);

        return purchaseOrders
            .Select(order => ToResponse(
                order,
                workflows.GetValueOrDefault(order.Id)))
            .ToList();
    }

    public async Task<PurchaseOrderResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await ReadQuery()
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (purchaseOrder is null)
        {
            return null;
        }

        var workflow = await workflowEngine.GetInstanceAsync(
            PurchaseOrderWorkflow.EntityType,
            id,
            cancellationToken);

        return ToResponse(purchaseOrder, workflow);
    }

    public async Task<PurchaseOrderOperationResult> CreateAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId < 1)
        {
            return Unauthorized("An authenticated user is required.");
        }

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

        var workflowResult = await workflowEngine.StartAsync(
            PurchaseOrderWorkflow.EntityType,
            purchaseOrder.Id,
            ToWorkflowActor(),
            cancellationToken);
        if (workflowResult.Status != WorkflowExecutionStatus.Success)
        {
            await transaction.RollbackAsync(cancellationToken);
            return WorkflowFailure(workflowResult);
        }

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

    public async Task<PurchaseOrderOperationResult> ExecuteActionAsync(
        int id,
        string actionCode,
        PurchaseOrderActionRequest request,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.PurchaseOrders
            .Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (purchaseOrder is null)
        {
            return NotFound();
        }

        var normalizedActionCode = actionCode.Trim().ToUpperInvariant();
        if (normalizedActionCode == PurchaseOrderWorkflow.SubmitAction)
        {
            if (purchaseOrder.Status != PurchaseOrderStatus.Draft)
            {
                return InvalidState("Only a draft purchase order can be submitted.");
            }

            var validationMessage = ValidateForSubmission(purchaseOrder);
            if (validationMessage is not null)
            {
                return ValidationFailed(validationMessage);
            }
        }
        else if ((normalizedActionCode is PurchaseOrderWorkflow.ApproveAction or
                  PurchaseOrderWorkflow.RejectAction) &&
                 purchaseOrder.Status != PurchaseOrderStatus.PendingApproval)
        {
            return InvalidState("Only a purchase order pending approval can be approved or rejected.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var workflow = await workflowEngine.GetInstanceAsync(
            PurchaseOrderWorkflow.EntityType,
            id,
            cancellationToken);
        if (workflow is null && normalizedActionCode == PurchaseOrderWorkflow.SubmitAction)
        {
            var startResult = await workflowEngine.StartAsync(
                PurchaseOrderWorkflow.EntityType,
                id,
                new WorkflowActor(
                    purchaseOrder.CreatedByUserId,
                    purchaseOrder.CreatedByName,
                    []),
                cancellationToken);
            if (startResult.Status != WorkflowExecutionStatus.Success)
            {
                await transaction.RollbackAsync(cancellationToken);
                return WorkflowFailure(startResult);
            }
        }

        var workflowResult = await workflowEngine.ExecuteActionAsync(
            PurchaseOrderWorkflow.EntityType,
            id,
            normalizedActionCode,
            ToWorkflowActor(),
            request.Comment,
            cancellationToken);
        if (workflowResult.Status != WorkflowExecutionStatus.Success)
        {
            await transaction.RollbackAsync(cancellationToken);
            return WorkflowFailure(workflowResult);
        }

        purchaseOrder.Status = MapWorkflowStepToStatus(
            workflowResult.Workflow!.CurrentStepCode);
        purchaseOrder.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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

        if (purchaseOrder.Status != PurchaseOrderStatus.Approved)
        {
            return InvalidState("Only approved purchase orders can be issued.");
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

        var renderedEmail = await emailRenderer.RenderPurchaseOrderAsync(
            purchaseOrder,
            supplierEmail,
            cancellationToken);
        if (!renderedEmail.IsSuccess)
        {
            return InvalidState(renderedEmail.ErrorMessage!);
        }

        var rendered = renderedEmail.RenderedEmail!;
        var email = rendered.Message;
        var now = DateTimeOffset.UtcNow;
        var issuedByName = CurrentUserName();
        byte[] pdfContent;
        try
        {
            pdfContent = pdfGenerator.Generate(purchaseOrder, issuedByName, now);
            if (pdfContent.Length == 0)
            {
                return InvalidState(
                    "The Purchase Order PDF could not be generated. The order remains Approved.");
            }
        }
        catch (Exception)
        {
            return InvalidState(
                "The Purchase Order PDF could not be generated. The order remains Approved.");
        }

        purchaseOrder.Status = PurchaseOrderStatus.Issued;
        purchaseOrder.IssuedAtUtc = now;
        purchaseOrder.IssuedByUserId = currentUser.UserId;
        purchaseOrder.IssuedByName = issuedByName;
        purchaseOrder.UpdatedAtUtc = now;

        var emailOutbox = new EmailOutbox
        {
            SourceType = "PurchaseOrder",
            SourceId = purchaseOrder.Id,
            SourceReference = purchaseOrder.PurchaseOrderNumber,
            FromAddress = smtp.FromAddress,
            FromName = smtp.FromName,
            RecipientEmail = email.RecipientEmail,
            CcRecipients = email.CcRecipients,
            BccRecipients = email.BccRecipients,
            Subject = email.Subject,
            HtmlBody = email.HtmlBody,
            TemplateCode = rendered.TemplateCode,
            TemplateVersion = rendered.TemplateVersion,
            Status = EmailDeliveryStatus.Pending,
            CreatedByUserId = currentUser.UserId,
            CreatedByName = issuedByName,
            CreatedDate = DateOnly.FromDateTime(now.UtcDateTime),
            CreatedAtUtc = now
        };
        emailOutbox.Attachments.Add(new EmailAttachment
        {
            FileName = $"Purchase-Order-{purchaseOrder.PurchaseOrderNumber}.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = pdfContent.LongLength,
            Content = pdfContent,
            CreatedAtUtc = now
        });
        purchaseOrder.EmailOutboxes.Add(emailOutbox);

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

        if (purchaseOrder.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Issued))
        {
            return InvalidState("Only approved or issued purchase orders can be cancelled.");
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

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await workflowEngine.DeleteInstanceAsync(
            PurchaseOrderWorkflow.EntityType,
            id,
            cancellationToken);
        dbContext.PurchaseOrders.Remove(purchaseOrder);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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

    private static PurchaseOrderResponse ToResponse(
        PurchaseOrder purchaseOrder,
        WorkflowInstanceResponse? workflow)
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
            items,
            workflow);
    }

    private static string? ValidateForSubmission(PurchaseOrder purchaseOrder)
    {
        if (!purchaseOrder.ExpectedDeliveryDate.HasValue)
        {
            return "Expected delivery date is required before submission.";
        }

        if (purchaseOrder.ExpectedDeliveryDate.Value < purchaseOrder.OrderDate)
        {
            return "Expected delivery date must be on or after the order date.";
        }

        if (string.IsNullOrWhiteSpace(purchaseOrder.DeliveryAddress))
        {
            return "Delivery address is required before submission.";
        }

        if (purchaseOrder.Items.Count == 0 ||
            purchaseOrder.Items.Any(item => item.Quantity <= 0 || item.UnitPrice <= 0))
        {
            return "The purchase order must contain valid items before submission.";
        }

        return null;
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

    private WorkflowActor ToWorkflowActor() => new(
        currentUser.UserId,
        CurrentUserName(),
        currentUser.Roles);

    private static PurchaseOrderStatus MapWorkflowStepToStatus(string stepCode) =>
        stepCode switch
        {
            PurchaseOrderWorkflow.DraftStep => PurchaseOrderStatus.Draft,
            PurchaseOrderWorkflow.PendingApprovalStep => PurchaseOrderStatus.PendingApproval,
            PurchaseOrderWorkflow.ApprovedStep => PurchaseOrderStatus.Approved,
            _ => throw new InvalidOperationException(
                $"Unsupported Purchase Order workflow step '{stepCode}'.")
        };

    private static PurchaseOrderOperationResult WorkflowFailure(WorkflowExecutionResult result) =>
        new(
            result.Status switch
            {
                WorkflowExecutionStatus.ActionNotAvailable =>
                    PurchaseOrderOperationStatus.InvalidState,
                WorkflowExecutionStatus.Unauthorized =>
                    PurchaseOrderOperationStatus.Unauthorized,
                WorkflowExecutionStatus.CommentRequired =>
                    PurchaseOrderOperationStatus.ValidationFailed,
                _ => PurchaseOrderOperationStatus.WorkflowUnavailable
            },
            ErrorMessage: result.ErrorMessage);

    private static string CreateTemporaryNumber() => $"TMP-{Guid.NewGuid():N}"[..20];

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PurchaseOrderOperationResult NotFound(string? message = null) =>
        new(PurchaseOrderOperationStatus.NotFound, ErrorMessage: message);

    private static PurchaseOrderOperationResult ValidationFailed(string message) =>
        new(PurchaseOrderOperationStatus.ValidationFailed, ErrorMessage: message);

    private static PurchaseOrderOperationResult InvalidState(string message) =>
        new(PurchaseOrderOperationStatus.InvalidState, ErrorMessage: message);

    private static PurchaseOrderOperationResult Unauthorized(string message) =>
        new(PurchaseOrderOperationStatus.Unauthorized, ErrorMessage: message);

    private static PurchaseOrderOperationResult SupplierUnavailable() =>
        new(
            PurchaseOrderOperationStatus.SupplierUnavailable,
            ErrorMessage: "The selected supplier is inactive.");
}
