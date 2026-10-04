using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.Dashboard;
using Project1.Api.Entities;
using Project1.Api.Entities.Workflows;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Services.Dashboard;

public sealed class DashboardService(
    AppDbContext dbContext,
    ICurrentUserContext currentUser) : IDashboardService
{
    private const string PurchaseRequestEntityType = "PurchaseRequest";
    private const string PurchaseOrderEntityType = "PurchaseOrder";

    public async Task<DashboardResponse> GetAsync(CancellationToken cancellationToken)
    {
        var roles = currentUser.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isAdmin = roles.Contains(ApplicationRoles.Admin);
        var isRequester = roles.Contains(ApplicationRoles.Requester);
        var isDepartmentApprover = roles.Contains(ApplicationRoles.DepartmentApprover);
        var isFinanceApprover = roles.Contains(ApplicationRoles.FinanceApprover);
        var isProcurement = roles.Contains(ApplicationRoles.ProcurementOfficer);
        var isPurchaseOrderApprover = roles.Contains(ApplicationRoles.PurchaseOrderApprover);
        var isWarehouse = roles.Contains(ApplicationRoles.WarehouseOfficer);
        var isCatalog = roles.Contains(ApplicationRoles.CatalogManager);

        var purchaseRequests = await LoadPurchaseRequestsAsync(cancellationToken);
        var quotations = await dbContext.Quotations
            .AsNoTracking()
            .Select(quotation => new QuotationSnapshot(
                quotation.Id,
                quotation.QuotationNumber,
                quotation.PurchaseRequestId,
                quotation.Status,
                quotation.PurchaseOrder != null,
                quotation.CreatedByName,
                quotation.CreatedAtUtc,
                quotation.UpdatedAtUtc,
                quotation.SubmittedAtUtc,
                quotation.SelectedAtUtc))
            .ToListAsync(cancellationToken);
        var purchaseOrders = await dbContext.PurchaseOrders
            .AsNoTracking()
            .Select(order => new PurchaseOrderSnapshot(
                order.Id,
                order.PurchaseOrderNumber,
                order.Status,
                order.CreatedByUserId,
                order.CreatedByName,
                order.CreatedAtUtc,
                order.UpdatedAtUtc,
                order.IssuedAtUtc,
                order.IssuedByName))
            .ToListAsync(cancellationToken);
        var goodsReceipts = await dbContext.GoodsReceipts
            .AsNoTracking()
            .Select(receipt => new GoodsReceiptSnapshot(
                receipt.Id,
                receipt.GoodsReceiptNumber,
                receipt.Status,
                receipt.CreatedByUserId,
                receipt.CreatedByName,
                receipt.CreatedAtUtc,
                receipt.UpdatedAtUtc,
                receipt.PostedAtUtc,
                receipt.PostedByName))
            .ToListAsync(cancellationToken);
        var emailRecords = await dbContext.EmailOutboxes
            .AsNoTracking()
            .Select(email => new EmailSnapshot(
                email.Id,
                email.SourceReference,
                email.Status,
                email.CreatedByName,
                email.CreatedAtUtc,
                email.UpdatedAtUtc,
                email.LastAttemptAtUtc,
                email.SentAtUtc))
            .ToListAsync(cancellationToken);
        var products = await dbContext.Products
            .AsNoTracking()
            .Select(product => new ProductSnapshot(
                product.Id,
                product.Code,
                product.Name,
                product.IsActive,
                product.ReorderLevel,
                product.InventoryBalance == null ? 0m : product.InventoryBalance.QuantityOnHand,
                product.CreatedAtUtc,
                product.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var cards = new Dictionary<string, DashboardSummaryCardResponse>(StringComparer.Ordinal);
        var reminders = new Dictionary<string, DashboardReminderResponse>(StringComparer.Ordinal);

        if (isAdmin)
        {
            BuildAdminDashboard(cards, reminders, purchaseRequests, purchaseOrders, emailRecords, products);
        }
        else
        {
            if (isRequester)
            {
                BuildRequesterDashboard(cards, reminders, purchaseRequests);
            }

            if (isDepartmentApprover)
            {
                var count = purchaseRequests.Count(request => request.StepCode == "DEPARTMENT_REVIEW");
                AddCard(cards, "department-review", "Department review", count,
                    "Purchase requests waiting for department approval.", "/my-tasks", "warning");
                AddReminder(reminders, "department-review", "Department approvals waiting",
                    "Review the department need and approve or reject each request.", count,
                    "/my-tasks", "warning");
            }

            if (isFinanceApprover)
            {
                var count = purchaseRequests.Count(request => request.StepCode == "FINANCE_REVIEW");
                AddCard(cards, "finance-review", "Finance review", count,
                    "Purchase requests waiting for budget approval.", "/my-tasks", "warning");
                AddReminder(reminders, "finance-review", "Finance approvals waiting",
                    "Confirm budget availability for requests at Finance Review.", count,
                    "/my-tasks", "warning");
            }

            if (isProcurement)
            {
                BuildProcurementDashboard(
                    cards,
                    reminders,
                    purchaseRequests,
                    quotations,
                    purchaseOrders,
                    emailRecords);
            }

            if (isPurchaseOrderApprover)
            {
                var count = purchaseOrders.Count(order => order.Status == PurchaseOrderStatus.PendingApproval);
                AddCard(cards, "po-approval", "PO approval", count,
                    "Purchase orders waiting for your approval decision.", "/my-tasks", "warning");
                AddReminder(reminders, "po-approval", "Purchase Orders need approval",
                    "Review supplier, pricing, delivery, and total before approval.", count,
                    "/my-tasks", "warning");
            }

            if (isWarehouse)
            {
                BuildWarehouseDashboard(cards, reminders, purchaseOrders, goodsReceipts, products);
            }

            if (isCatalog)
            {
                BuildCatalogDashboard(cards, reminders, products);
            }
        }

        var activities = await BuildRecentActivityAsync(
            isAdmin,
            isRequester,
            isDepartmentApprover,
            isFinanceApprover,
            isProcurement,
            isPurchaseOrderApprover,
            isWarehouse,
            isCatalog,
            purchaseRequests,
            quotations,
            purchaseOrders,
            goodsReceipts,
            emailRecords,
            products,
            cancellationToken);

        return new DashboardResponse(
            cards.Values.ToList(),
            reminders.Values.ToList(),
            activities,
            DateTimeOffset.UtcNow);
    }

    private async Task<List<PurchaseRequestSnapshot>> LoadPurchaseRequestsAsync(
        CancellationToken cancellationToken)
    {
        return await (
            from request in dbContext.PurchaseRequests.AsNoTracking()
            join instance in dbContext.WorkflowProcessInstances.AsNoTracking()
                on new { EntityType = PurchaseRequestEntityType, EntityId = request.Id }
                equals new { instance.EntityType, instance.EntityId }
            join step in dbContext.WorkflowStepInstances.AsNoTracking()
                on instance.CurrentStepInstanceId equals step.Id
            select new PurchaseRequestSnapshot(
                request.Id,
                request.RequestNumber,
                request.RequesterUserId,
                instance.Status,
                step.Code,
                request.CreatedAtUtc,
                request.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private void BuildRequesterDashboard(
        IDictionary<string, DashboardSummaryCardResponse> cards,
        IDictionary<string, DashboardReminderResponse> reminders,
        IReadOnlyCollection<PurchaseRequestSnapshot> purchaseRequests)
    {
        var ownRequests = purchaseRequests
            .Where(request => request.RequesterUserId == currentUser.UserId)
            .ToList();
        var drafts = ownRequests.Count(request => request.StepCode == "DRAFT");
        var inReview = ownRequests.Count(request =>
            request.WorkflowStatus == WorkflowInstanceStatus.Running && request.StepCode != "DRAFT");
        var approved = ownRequests.Count(request => request.StepCode == "APPROVED");
        var rejected = ownRequests.Count(request => request.StepCode == "REJECTED");

        AddCard(cards, "my-drafts", "My drafts", drafts,
            "Purchase requests saved but not submitted.", "/my-tasks", "neutral");
        AddCard(cards, "my-in-review", "In review", inReview,
            "Your requests currently moving through approval.", "/purchase-requests", "warning");
        AddCard(cards, "my-approved", "Approved", approved,
            "Your completed and approved requests.", "/purchase-requests", "positive");
        AddCard(cards, "my-rejected", "Rejected", rejected,
            "Your requests that completed as rejected.", "/purchase-requests", "danger");
        AddReminder(reminders, "my-drafts", "Draft requests need submission",
            "Finish the request details and submit them for Department Review.", drafts,
            "/my-tasks", "info");
    }

    private static void BuildProcurementDashboard(
        IDictionary<string, DashboardSummaryCardResponse> cards,
        IDictionary<string, DashboardReminderResponse> reminders,
        IReadOnlyCollection<PurchaseRequestSnapshot> purchaseRequests,
        IReadOnlyCollection<QuotationSnapshot> quotations,
        IReadOnlyCollection<PurchaseOrderSnapshot> purchaseOrders,
        IReadOnlyCollection<EmailSnapshot> emailRecords)
    {
        var quotationRequestIds = quotations.Select(quotation => quotation.PurchaseRequestId).ToHashSet();
        var readyForQuotation = purchaseRequests.Count(request =>
            request.StepCode == "APPROVED" && !quotationRequestIds.Contains(request.Id));
        var comparisonPending = quotations
            .Where(quotation => quotation.Status == QuotationStatus.Submitted)
            .Select(quotation => quotation.PurchaseRequestId)
            .Distinct()
            .Count();
        var selectedWithoutOrder = quotations.Count(quotation =>
            quotation.Status == QuotationStatus.Selected && !quotation.HasPurchaseOrder);
        var approvedOrders = purchaseOrders.Count(order => order.Status == PurchaseOrderStatus.Approved);
        var failedEmails = emailRecords.Count(email => email.Status == EmailDeliveryStatus.Failed);

        AddCard(cards, "ready-for-quotation", "Ready for quotation", readyForQuotation,
            "Approved requests that have no supplier quotation yet.", "/quotations", "neutral");
        AddCard(cards, "quotation-comparison", "Compare quotations", comparisonPending,
            "Requests with submitted quotations awaiting selection.", "/quotations", "warning");
        AddCard(cards, "selected-without-order", "Create Purchase Order", selectedWithoutOrder,
            "Selected quotations that have no Purchase Order.", "/purchase-orders", "warning");
        AddCard(cards, "approved-orders", "Ready to issue", approvedOrders,
            "Approved Purchase Orders ready for supplier issue.", "/purchase-orders", "positive");
        AddCard(cards, "failed-emails", "Failed emails", failedEmails,
            "Supplier emails that require investigation or retry.", "/email-records", "danger");

        AddReminder(reminders, "ready-for-quotation", "Approved requests need quotations",
            "Record supplier offers for newly approved Purchase Requests.", readyForQuotation,
            "/quotations", "info");
        AddReminder(reminders, "quotation-comparison", "Quotation comparisons are pending",
            "Compare submitted supplier prices and select a winner.", comparisonPending,
            "/quotations", "warning");
        AddReminder(reminders, "selected-without-order", "Selected quotations need orders",
            "Create a Purchase Order from each selected quotation.", selectedWithoutOrder,
            "/purchase-orders", "warning");
        AddReminder(reminders, "approved-orders", "Approved orders are ready to issue",
            "Issue the order to create its supplier email and PDF attachment.", approvedOrders,
            "/purchase-orders", "info");
        AddReminder(reminders, "failed-emails", "Supplier email delivery failed",
            "Check the saved error and retry after correcting SMTP or recipient details.", failedEmails,
            "/email-records", "critical");
    }

    private static void BuildWarehouseDashboard(
        IDictionary<string, DashboardSummaryCardResponse> cards,
        IDictionary<string, DashboardReminderResponse> reminders,
        IReadOnlyCollection<PurchaseOrderSnapshot> purchaseOrders,
        IReadOnlyCollection<GoodsReceiptSnapshot> goodsReceipts,
        IReadOnlyCollection<ProductSnapshot> products)
    {
        var readyToReceive = purchaseOrders.Count(order =>
            order.Status is PurchaseOrderStatus.Issued or PurchaseOrderStatus.PartiallyReceived);
        var draftReceipts = goodsReceipts.Count(receipt => receipt.Status == GoodsReceiptStatus.Draft);
        var lowStock = products.Count(product =>
            product.IsActive && product.QuantityOnHand > 0m && product.QuantityOnHand <= product.ReorderLevel);
        var outOfStock = products.Count(product => product.IsActive && product.QuantityOnHand == 0m);

        AddCard(cards, "ready-to-receive", "Ready to receive", readyToReceive,
            "Issued or partially received Purchase Orders.", "/goods-receipts", "warning");
        AddCard(cards, "draft-receipts", "Draft receipts", draftReceipts,
            "Goods Receipts waiting to be checked and posted.", "/goods-receipts", "neutral");
        AddCard(cards, "low-stock", "Low stock", lowStock,
            "Active Products at or below their reorder level.", "/inventory", "warning");
        AddCard(cards, "out-of-stock", "Out of stock", outOfStock,
            "Active Products with no quantity on hand.", "/inventory", "danger");

        AddReminder(reminders, "ready-to-receive", "Supplier deliveries can be received",
            "Create a Goods Receipt when stock arrives for an issued order.", readyToReceive,
            "/goods-receipts", "info");
        AddReminder(reminders, "draft-receipts", "Draft Goods Receipts need posting",
            "Verify physical quantities, then post the Draft to update Inventory.", draftReceipts,
            "/goods-receipts", "warning");
        AddReminder(reminders, "low-stock", "Products are running low",
            "Review Products that have reached their reorder level.", lowStock,
            "/inventory", "warning");
        AddReminder(reminders, "out-of-stock", "Products are out of stock",
            "These active Products currently have zero quantity available.", outOfStock,
            "/inventory", "critical");
    }

    private static void BuildCatalogDashboard(
        IDictionary<string, DashboardSummaryCardResponse> cards,
        IDictionary<string, DashboardReminderResponse> reminders,
        IReadOnlyCollection<ProductSnapshot> products)
    {
        var active = products.Count(product => product.IsActive);
        var inactive = products.Count(product => !product.IsActive);
        var lowStock = products.Count(product =>
            product.IsActive && product.QuantityOnHand > 0m && product.QuantityOnHand <= product.ReorderLevel);
        var outOfStock = products.Count(product => product.IsActive && product.QuantityOnHand == 0m);

        AddCard(cards, "active-products", "Active Products", active,
            "Products currently available for new Purchase Requests.", "/products", "positive");
        AddCard(cards, "low-stock", "Low stock", lowStock,
            "Products at or below their configured reorder level.", "/products", "warning");
        AddCard(cards, "out-of-stock", "Out of stock", outOfStock,
            "Active Products with no stock available.", "/products", "danger");
        AddCard(cards, "inactive-products", "Inactive Products", inactive,
            "Products retained only for historical records.", "/products", "neutral");

        AddReminder(reminders, "low-stock", "Review low-stock Product settings",
            "Confirm reorder levels and Product details are still correct.", lowStock,
            "/products", "warning");
        AddReminder(reminders, "out-of-stock", "Active Products have no stock",
            "Review the affected Product records and coordinate replenishment.", outOfStock,
            "/products", "critical");
    }

    private static void BuildAdminDashboard(
        IDictionary<string, DashboardSummaryCardResponse> cards,
        IDictionary<string, DashboardReminderResponse> reminders,
        IReadOnlyCollection<PurchaseRequestSnapshot> purchaseRequests,
        IReadOnlyCollection<PurchaseOrderSnapshot> purchaseOrders,
        IReadOnlyCollection<EmailSnapshot> emailRecords,
        IReadOnlyCollection<ProductSnapshot> products)
    {
        var pendingRequests = purchaseRequests.Count(request =>
            request.StepCode is "DEPARTMENT_REVIEW" or "FINANCE_REVIEW");
        var pendingOrders = purchaseOrders.Count(order => order.Status == PurchaseOrderStatus.PendingApproval);
        var readyToReceive = purchaseOrders.Count(order =>
            order.Status is PurchaseOrderStatus.Issued or PurchaseOrderStatus.PartiallyReceived);
        var lowStock = products.Count(product =>
            product.IsActive && product.QuantityOnHand > 0m && product.QuantityOnHand <= product.ReorderLevel);
        var outOfStock = products.Count(product => product.IsActive && product.QuantityOnHand == 0m);
        var failedEmails = emailRecords.Count(email => email.Status == EmailDeliveryStatus.Failed);

        AddCard(cards, "pending-request-approval", "PR approvals", pendingRequests,
            "Purchase Requests waiting at Department or Finance Review.", "/my-tasks", "warning");
        AddCard(cards, "po-approval", "PO approvals", pendingOrders,
            "Purchase Orders waiting for approval.", "/my-tasks", "warning");
        AddCard(cards, "ready-to-receive", "Ready to receive", readyToReceive,
            "Issued or partially received Purchase Orders.", "/goods-receipts", "neutral");
        AddCard(cards, "low-stock", "Low stock", lowStock,
            "Active Products at or below reorder level.", "/inventory", "warning");
        AddCard(cards, "out-of-stock", "Out of stock", outOfStock,
            "Active Products with zero quantity.", "/inventory", "danger");
        AddCard(cards, "failed-emails", "Failed emails", failedEmails,
            "Supplier messages requiring attention.", "/email-records", "danger");

        AddReminder(reminders, "pending-request-approval", "Purchase Request approvals are waiting",
            "Open My Tasks to review Department and Finance approval queues.", pendingRequests,
            "/my-tasks", "warning");
        AddReminder(reminders, "po-approval", "Purchase Order approvals are waiting",
            "Review orders before Procurement issues them to Suppliers.", pendingOrders,
            "/my-tasks", "warning");
        AddReminder(reminders, "ready-to-receive", "Orders are waiting for delivery",
            "Warehouse can receive stock against these orders.", readyToReceive,
            "/goods-receipts", "info");
        AddReminder(reminders, "low-stock", "Products are below reorder level",
            "Review Inventory and plan replenishment.", lowStock,
            "/inventory", "warning");
        AddReminder(reminders, "out-of-stock", "Products are out of stock",
            "Active Products with zero quantity need attention.", outOfStock,
            "/inventory", "critical");
        AddReminder(reminders, "failed-emails", "Email delivery failures need attention",
            "Review errors and retry eligible Purchase Order emails.", failedEmails,
            "/email-records", "critical");
    }

    private async Task<IReadOnlyList<DashboardActivityResponse>> BuildRecentActivityAsync(
        bool isAdmin,
        bool isRequester,
        bool isDepartmentApprover,
        bool isFinanceApprover,
        bool isProcurement,
        bool isPurchaseOrderApprover,
        bool isWarehouse,
        bool isCatalog,
        IReadOnlyCollection<PurchaseRequestSnapshot> purchaseRequests,
        IReadOnlyCollection<QuotationSnapshot> quotations,
        IReadOnlyCollection<PurchaseOrderSnapshot> purchaseOrders,
        IReadOnlyCollection<GoodsReceiptSnapshot> goodsReceipts,
        IReadOnlyCollection<EmailSnapshot> emailRecords,
        IReadOnlyCollection<ProductSnapshot> products,
        CancellationToken cancellationToken)
    {
        var activities = new Dictionary<string, DashboardActivityResponse>(StringComparer.Ordinal);

        var canSeeAllPurchaseRequests = isAdmin || isDepartmentApprover || isFinanceApprover || isProcurement;
        var purchaseRequestReferences = purchaseRequests
            .Where(request => canSeeAllPurchaseRequests ||
                              (isRequester && request.RequesterUserId == currentUser.UserId))
            .ToDictionary(request => request.Id, request => request.Number);
        if (purchaseRequestReferences.Count > 0 && (canSeeAllPurchaseRequests || isRequester))
        {
            foreach (var activity in await LoadWorkflowActivityAsync(
                         PurchaseRequestEntityType,
                         "Purchase Request",
                         "/purchase-requests",
                         purchaseRequestReferences,
                         cancellationToken))
            {
                activities.TryAdd(activity.Key, activity);
            }
        }

        if (isAdmin || isProcurement || isPurchaseOrderApprover)
        {
            var orderReferences = purchaseOrders.ToDictionary(order => order.Id, order => order.Number);
            foreach (var activity in await LoadWorkflowActivityAsync(
                         PurchaseOrderEntityType,
                         "Purchase Order",
                         "/purchase-orders",
                         orderReferences,
                         cancellationToken))
            {
                activities.TryAdd(activity.Key, activity);
            }

            foreach (var order in purchaseOrders.Where(order => order.IssuedAtUtc.HasValue))
            {
                var activity = new DashboardActivityResponse(
                    $"purchase-order-issued:{order.Id}",
                    "Purchase Order",
                    order.Number,
                    "Purchase Order issued",
                    "Supplier email and PDF were queued for delivery.",
                    order.IssuedByName ?? "System",
                    order.IssuedAtUtc!.Value,
                    "/purchase-orders");
                activities.TryAdd(activity.Key, activity);
            }
        }

        if (isAdmin || isProcurement)
        {
            foreach (var quotation in quotations)
            {
                var occurredAt = quotation.SelectedAtUtc ?? quotation.SubmittedAtUtc ??
                    quotation.UpdatedAtUtc ?? quotation.CreatedAtUtc;
                var title = quotation.Status switch
                {
                    QuotationStatus.Selected => "Quotation selected",
                    QuotationStatus.NotSelected => "Quotation not selected",
                    QuotationStatus.Submitted => "Quotation submitted",
                    _ => "Quotation draft saved"
                };
                var activity = new DashboardActivityResponse(
                    $"quotation:{quotation.Id}:{quotation.Status}",
                    "Supplier Quotation",
                    quotation.Number,
                    title,
                    "Supplier quotation status is " + SplitWords(quotation.Status.ToString()) + ".",
                    quotation.CreatedByName,
                    occurredAt,
                    "/quotations");
                activities.TryAdd(activity.Key, activity);
            }

            foreach (var email in emailRecords)
            {
                var occurredAt = email.SentAtUtc ?? email.LastAttemptAtUtc ??
                    email.UpdatedAtUtc ?? email.CreatedAtUtc;
                var title = email.Status switch
                {
                    EmailDeliveryStatus.Sent => "Supplier email sent",
                    EmailDeliveryStatus.Failed => "Supplier email failed",
                    _ => "Supplier email queued"
                };
                var actor = email.Status == EmailDeliveryStatus.Pending
                    ? email.CreatedByName ?? "System"
                    : "Email worker";
                var activity = new DashboardActivityResponse(
                    $"email:{email.Id}:{email.Status}",
                    "Email Record",
                    email.Reference,
                    title,
                    "Email delivery status is " + email.Status + ".",
                    actor,
                    occurredAt,
                    "/email-records");
                activities.TryAdd(activity.Key, activity);
            }
        }

        if (isAdmin || isWarehouse || isProcurement)
        {
            foreach (var receipt in goodsReceipts)
            {
                var posted = receipt.Status == GoodsReceiptStatus.Posted;
                var activity = new DashboardActivityResponse(
                    $"goods-receipt:{receipt.Id}:{receipt.Status}",
                    "Goods Receipt",
                    receipt.Number,
                    posted ? "Goods Receipt posted" : "Goods Receipt draft saved",
                    posted
                        ? "Inventory was updated from a supplier delivery."
                        : "Delivery details are waiting to be posted.",
                    posted ? receipt.PostedByName ?? "System" : receipt.CreatedByName,
                    receipt.PostedAtUtc ?? receipt.UpdatedAtUtc ?? receipt.CreatedAtUtc,
                    "/goods-receipts");
                activities.TryAdd(activity.Key, activity);
            }
        }

        if (isAdmin || isCatalog)
        {
            foreach (var product in products)
            {
                var updated = product.UpdatedAtUtc.HasValue;
                var activity = new DashboardActivityResponse(
                    $"product:{product.Id}:{product.UpdatedAtUtc?.Ticks ?? 0}",
                    "Product",
                    product.Code,
                    updated ? "Product updated" : "Product created",
                    product.Name + (product.IsActive ? " is active." : " is inactive."),
                    "Catalog",
                    product.UpdatedAtUtc ?? product.CreatedAtUtc,
                    "/products");
                activities.TryAdd(activity.Key, activity);
            }
        }

        return activities.Values
            .OrderByDescending(activity => activity.OccurredAtUtc)
            .ThenByDescending(activity => activity.Key, StringComparer.Ordinal)
            .Take(10)
            .ToList();
    }

    private async Task<IReadOnlyList<DashboardActivityResponse>> LoadWorkflowActivityAsync(
        string entityType,
        string module,
        string route,
        IReadOnlyDictionary<int, string> references,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            return [];
        }

        var entityIds = references.Keys.ToArray();
        var history = await dbContext.WorkflowHistory
            .AsNoTracking()
            .Where(item =>
                item.ProcessInstance.EntityType == entityType &&
                entityIds.Contains(item.ProcessInstance.EntityId))
            .OrderByDescending(item => item.Id)
            .Take(50)
            .Select(item => new
            {
                item.Id,
                item.ProcessInstance.EntityId,
                item.ActionCode,
                item.FromStepCode,
                item.ToStepCode,
                item.ActionBy,
                item.ActionAtUtc
            })
            .ToListAsync(cancellationToken);

        return history
            .OrderByDescending(item => item.ActionAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(20)
            .Select(item => new DashboardActivityResponse(
                $"workflow:{entityType}:{item.Id}",
                module,
                references[item.EntityId],
                WorkflowActionTitle(item.ActionCode),
                item.FromStepCode is null
                    ? "Workflow started at " + SplitWords(item.ToStepCode) + "."
                    : SplitWords(item.FromStepCode) + " → " + SplitWords(item.ToStepCode) + ".",
                item.ActionBy,
                item.ActionAtUtc,
                route))
            .ToList();
    }

    private static string WorkflowActionTitle(string actionCode) => actionCode switch
    {
        "START" => "Workflow started",
        "SUBMIT" => "Submitted for approval",
        "APPROVE" => "Approval completed",
        "REJECT" => "Request rejected",
        _ => SplitWords(actionCode)
    };

    private static string SplitWords(string value) =>
        string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

    private static void AddCard(
        IDictionary<string, DashboardSummaryCardResponse> cards,
        string key,
        string label,
        int value,
        string description,
        string route,
        string tone) =>
        cards.TryAdd(key, new DashboardSummaryCardResponse(
            key, label, value, description, route, tone));

    private static void AddReminder(
        IDictionary<string, DashboardReminderResponse> reminders,
        string key,
        string title,
        string description,
        int count,
        string route,
        string severity)
    {
        if (count > 0)
        {
            reminders.TryAdd(key, new DashboardReminderResponse(
                key, title, description, count, route, severity));
        }
    }

    private sealed record PurchaseRequestSnapshot(
        int Id,
        string Number,
        int? RequesterUserId,
        WorkflowInstanceStatus WorkflowStatus,
        string StepCode,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc);

    private sealed record QuotationSnapshot(
        int Id,
        string Number,
        int PurchaseRequestId,
        QuotationStatus Status,
        bool HasPurchaseOrder,
        string CreatedByName,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        DateTimeOffset? SubmittedAtUtc,
        DateTimeOffset? SelectedAtUtc);

    private sealed record PurchaseOrderSnapshot(
        int Id,
        string Number,
        PurchaseOrderStatus Status,
        int CreatedByUserId,
        string CreatedByName,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        DateTimeOffset? IssuedAtUtc,
        string? IssuedByName);

    private sealed record GoodsReceiptSnapshot(
        int Id,
        string Number,
        GoodsReceiptStatus Status,
        int CreatedByUserId,
        string CreatedByName,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        DateTimeOffset? PostedAtUtc,
        string? PostedByName);

    private sealed record EmailSnapshot(
        int Id,
        string Reference,
        EmailDeliveryStatus Status,
        string? CreatedByName,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        DateTimeOffset? LastAttemptAtUtc,
        DateTimeOffset? SentAtUtc);

    private sealed record ProductSnapshot(
        int Id,
        string Code,
        string Name,
        bool IsActive,
        decimal ReorderLevel,
        decimal QuantityOnHand,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc);
}
