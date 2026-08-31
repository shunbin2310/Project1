using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.PurchaseRequests;
using Project1.Api.DTOs.Workflows;
using Project1.Api.Entities;
using Project1.Api.Services.Workflows;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Services.PurchaseRequests;

public sealed class PurchaseRequestService(
    AppDbContext dbContext,
    IWorkflowEngine workflowEngine,
    ICurrentUserContext currentUser) : IPurchaseRequestService
{
    private const string WorkflowEntityType = "PurchaseRequest";
    private const string DraftStepCode = "DRAFT";
    private const string SubmitActionCode = "SUBMIT";

    public async Task<IReadOnlyList<PurchaseRequestResponse>> GetAllAsync(
        string? stepCode,
        CancellationToken cancellationToken)
    {
        var requests = await ReadQuery()
            .OrderByDescending(request => request.Id)
            .ToListAsync(cancellationToken);
        var workflows = await workflowEngine.GetInstancesAsync(
            WorkflowEntityType,
            requests.Select(request => request.Id).ToList(),
            cancellationToken);

        return requests
            .Where(request => workflows.ContainsKey(request.Id))
            .Select(request => ToResponse(request, workflows[request.Id]))
            .Where(request => string.IsNullOrWhiteSpace(stepCode) ||
                string.Equals(
                    request.Workflow.CurrentStepCode,
                    stepCode.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<PurchaseRequestResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var request = await ReadQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (request is null)
        {
            return null;
        }

        var workflow = await workflowEngine.GetInstanceAsync(
            WorkflowEntityType,
            id,
            cancellationToken);

        return workflow is null ? null : ToResponse(request, workflow);
    }

    public async Task<PurchaseRequestOperationResult> CreateAsync(
        CreatePurchaseRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId < 1)
        {
            return Unauthorized("An authenticated user is required.");
        }

        var validation = ValidateDraftItems(request.Items);
        if (validation is not null)
        {
            return validation;
        }

        var departmentResult = await ValidateDepartmentExistsAsync(
            currentUser.DepartmentId,
            cancellationToken);
        if (departmentResult is not null)
        {
            return departmentResult;
        }

        var productResult = await LoadProductsAsync(request.Items, cancellationToken);
        if (productResult.Error is not null)
        {
            return productResult.Error;
        }

        var requesterName = currentUser.DisplayName.Trim();
        var purchaseRequest = new PurchaseRequest
        {
            RequestNumber = CreateTemporaryNumber(),
            RequesterName = requesterName,
            RequesterUserId = currentUser.UserId,
            DepartmentId = currentUser.DepartmentId,
            RequiredDate = request.RequiredDate,
            Justification = NormalizeOptionalText(request.Justification),
            Items = CreateItems(request.Items, productResult.Products!)
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.PurchaseRequests.Add(purchaseRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        purchaseRequest.RequestNumber = $"PR-{purchaseRequest.Id:D4}";
        await dbContext.SaveChangesAsync(cancellationToken);

        var workflowResult = await workflowEngine.StartAsync(
            WorkflowEntityType,
            purchaseRequest.Id,
            ToWorkflowActor(),
            cancellationToken);

        if (workflowResult.Status != WorkflowExecutionStatus.Success)
        {
            await transaction.RollbackAsync(cancellationToken);
            return WorkflowFailure(workflowResult);
        }

        await transaction.CommitAsync(cancellationToken);
        return await SuccessResultAsync(purchaseRequest.Id, cancellationToken);
    }

    public async Task<PurchaseRequestOperationResult> UpdateAsync(
        int id,
        UpdatePurchaseRequestRequest request,
        CancellationToken cancellationToken)
    {
        var workflow = await workflowEngine.GetInstanceAsync(
            WorkflowEntityType,
            id,
            cancellationToken);

        if (workflow is null)
        {
            return NotFound();
        }

        if (!string.Equals(workflow.CurrentStepCode, DraftStepCode, StringComparison.Ordinal))
        {
            return InvalidState("Only purchase requests at the DRAFT step can be edited.");
        }

        var purchaseRequest = await TrackedQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (purchaseRequest is null)
        {
            return NotFound();
        }

        if (!CanManageDraft(purchaseRequest))
        {
            return Unauthorized("Only the original requester can edit this draft.");
        }

        var validation = ValidateDraftItems(request.Items);
        if (validation is not null)
        {
            return validation;
        }

        var productResult = await LoadProductsAsync(request.Items, cancellationToken);
        if (productResult.Error is not null)
        {
            return productResult.Error;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.PurchaseRequestItems.RemoveRange(purchaseRequest.Items);
        purchaseRequest.Items = CreateItems(request.Items, productResult.Products!);
        purchaseRequest.RequiredDate = request.RequiredDate;
        purchaseRequest.Justification = NormalizeOptionalText(request.Justification);
        purchaseRequest.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await SuccessResultAsync(id, cancellationToken);
    }

    public async Task<PurchaseRequestOperationResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var workflow = await workflowEngine.GetInstanceAsync(
            WorkflowEntityType,
            id,
            cancellationToken);

        if (workflow is null)
        {
            return NotFound();
        }

        if (!string.Equals(workflow.CurrentStepCode, DraftStepCode, StringComparison.Ordinal))
        {
            return InvalidState("Only purchase requests at the DRAFT step can be deleted.");
        }

        var purchaseRequest = await dbContext.PurchaseRequests
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (purchaseRequest is null)
        {
            return NotFound();
        }

        if (!CanManageDraft(purchaseRequest))
        {
            return Unauthorized("Only the original requester can delete this draft.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await workflowEngine.DeleteInstanceAsync(WorkflowEntityType, id, cancellationToken);
        dbContext.PurchaseRequests.Remove(purchaseRequest);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PurchaseRequestOperationResult(PurchaseRequestOperationStatus.Success);
    }

    public async Task<PurchaseRequestOperationResult> ExecuteActionAsync(
        int id,
        string actionCode,
        PurchaseRequestActionRequest request,
        CancellationToken cancellationToken)
    {
        var purchaseRequest = await TrackedQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (purchaseRequest is null)
        {
            return NotFound();
        }

        var normalizedActionCode = actionCode.Trim().ToUpperInvariant();
        if (normalizedActionCode == SubmitActionCode)
        {
            var validationMessage = ValidateForSubmission(purchaseRequest);
            if (validationMessage is not null)
            {
                return ValidationFailed(validationMessage);
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var workflowResult = await workflowEngine.ExecuteActionAsync(
            WorkflowEntityType,
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

        purchaseRequest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await SuccessResultAsync(id, cancellationToken);
    }

    private IQueryable<PurchaseRequest> ReadQuery() =>
        dbContext.PurchaseRequests
            .AsNoTracking()
            .AsSplitQuery()
            .Include(request => request.Department)
            .Include(request => request.Items)
                .ThenInclude(item => item.Product)
                    .ThenInclude(product => product.UnitOfMeasure);

    private IQueryable<PurchaseRequest> TrackedQuery() =>
        dbContext.PurchaseRequests
            .AsSplitQuery()
            .Include(request => request.Department)
            .Include(request => request.Items)
                .ThenInclude(item => item.Product)
                    .ThenInclude(product => product.UnitOfMeasure);

    private static string? ValidateForSubmission(PurchaseRequest request)
    {
        if ((request.RequesterName?.Trim().Length ?? 0) < 2)
        {
            return "Requester name must contain at least 2 characters.";
        }

        if (request.Department is null || !request.Department.IsActive)
        {
            return "Select an active department before submitting.";
        }

        if (!request.RequiredDate.HasValue ||
            request.RequiredDate.Value < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return "Required date must be today or a future date.";
        }

        if (request.Items.Count == 0)
        {
            return "Add at least one item before submitting.";
        }

        if (request.Items.Any(item => !item.Product.IsActive))
        {
            return "All products must be active before submitting.";
        }

        if (request.Items.Any(item => item.Quantity <= 0))
        {
            return "Every item quantity must be greater than zero before submitting.";
        }

        return null;
    }

    private static PurchaseRequestOperationResult? ValidateDraftItems(
        IReadOnlyList<PurchaseRequestItemRequest> items)
    {
        if (items.GroupBy(item => item.ProductId).Any(group => group.Count() > 1))
        {
            return ValidationFailed("A product can only appear once in a purchase request.");
        }

        if (items.Any(item => item.ProductId < 1 || item.Quantity < 0 || item.EstimatedUnitPrice < 0))
        {
            return ValidationFailed("Purchase request item values are invalid.");
        }

        return null;
    }

    private async Task<PurchaseRequestOperationResult?> ValidateDepartmentExistsAsync(
        int? departmentId,
        CancellationToken cancellationToken)
    {
        if (!departmentId.HasValue)
        {
            return null;
        }

        var exists = await dbContext.Departments.AnyAsync(
            department => department.Id == departmentId.Value,
            cancellationToken);

        return exists
            ? null
            : new PurchaseRequestOperationResult(
                PurchaseRequestOperationStatus.DepartmentNotFound,
                ErrorMessage: "The selected department does not exist.");
    }

    private async Task<(
        IReadOnlyDictionary<int, Product>? Products,
        PurchaseRequestOperationResult? Error)> LoadProductsAsync(
        IReadOnlyList<PurchaseRequestItemRequest> items,
        CancellationToken cancellationToken)
    {
        var productIds = items.Select(item => item.ProductId).Distinct().ToList();
        var products = await dbContext.Products
            .Include(product => product.UnitOfMeasure)
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        if (products.Count != productIds.Count)
        {
            return (
                null,
                new PurchaseRequestOperationResult(
                    PurchaseRequestOperationStatus.ProductNotFound,
                    ErrorMessage: "One or more selected products do not exist."));
        }

        return (products, null);
    }

    private static List<PurchaseRequestItem> CreateItems(
        IReadOnlyList<PurchaseRequestItemRequest> requests,
        IReadOnlyDictionary<int, Product> products)
    {
        return requests.Select(request => new PurchaseRequestItem
        {
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            EstimatedUnitPrice = request.EstimatedUnitPrice ?? products[request.ProductId].DefaultUnitPrice
        }).ToList();
    }

    private async Task<PurchaseRequestOperationResult> SuccessResultAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var purchaseRequest = await GetByIdAsync(id, cancellationToken);
        return new PurchaseRequestOperationResult(
            PurchaseRequestOperationStatus.Success,
            purchaseRequest);
    }

    private static PurchaseRequestResponse ToResponse(
        PurchaseRequest request,
        WorkflowInstanceResponse workflow)
    {
        var items = request.Items
            .OrderBy(item => item.Id)
            .Select(item => new PurchaseRequestItemResponse(
                item.Id,
                item.ProductId,
                item.Product.Code,
                item.Product.Name,
                item.Product.UnitOfMeasure.Code,
                item.Quantity,
                item.EstimatedUnitPrice,
                item.Quantity * item.EstimatedUnitPrice))
            .ToList();

        return new PurchaseRequestResponse(
            request.Id,
            request.RequestNumber,
            request.RequesterName,
            request.DepartmentId,
            request.Department?.Code,
            request.Department?.Name,
            request.RequiredDate,
            request.Justification,
            items.Sum(item => item.LineTotal),
            request.CreatedAtUtc,
            request.UpdatedAtUtc,
            items,
            workflow);
    }

    private static PurchaseRequestOperationResult WorkflowFailure(WorkflowExecutionResult result) =>
        new(
            result.Status switch
            {
                WorkflowExecutionStatus.ActionNotAvailable =>
                    PurchaseRequestOperationStatus.InvalidState,
                WorkflowExecutionStatus.Unauthorized =>
                    PurchaseRequestOperationStatus.Unauthorized,
                WorkflowExecutionStatus.CommentRequired =>
                    PurchaseRequestOperationStatus.ValidationFailed,
                _ => PurchaseRequestOperationStatus.WorkflowUnavailable
            },
            ErrorMessage: result.ErrorMessage);

    private static string CreateTemporaryNumber() => $"TMP-{Guid.NewGuid():N}"[..20];

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private WorkflowActor ToWorkflowActor() => new(
        currentUser.UserId,
        currentUser.DisplayName,
        currentUser.Roles);

    private bool CanManageDraft(PurchaseRequest request) =>
        currentUser.UserId > 0 && request.RequesterUserId == currentUser.UserId;

    private static PurchaseRequestOperationResult NotFound() =>
        new(PurchaseRequestOperationStatus.NotFound);

    private static PurchaseRequestOperationResult InvalidState(string message) =>
        new(PurchaseRequestOperationStatus.InvalidState, ErrorMessage: message);

    private static PurchaseRequestOperationResult ValidationFailed(string message) =>
        new(PurchaseRequestOperationStatus.ValidationFailed, ErrorMessage: message);

    private static PurchaseRequestOperationResult Unauthorized(string message) =>
        new(PurchaseRequestOperationStatus.Unauthorized, ErrorMessage: message);
}
