using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.PurchaseOrders;
using Project1.Api.Entities;
using Project1.Api.Entities.Workflows;
using Project1.Api.Email;
using Project1.Api.Services.Authentication;
using Project1.Api.Services.PurchaseOrders;
using Project1.Api.Services.Workflows;

namespace Project1.Api.Tests.Services;

public sealed class PurchaseOrderServiceTests
{
    [Fact]
    public async Task ExecuteActionAsync_SubmitApproveAndRejectFollowWorkflowRules()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        fixture.CurrentUser.Set(
            10,
            "Procurement Officer",
            ApplicationRoles.ProcurementOfficer);
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        var submitted = await fixture.Service.ExecuteActionAsync(
            created.PurchaseOrder!.Id,
            PurchaseOrderWorkflow.SubmitAction,
            new PurchaseOrderActionRequest(),
            CancellationToken.None);

        fixture.CurrentUser.Set(
            11,
            "Purchase Order Approver",
            ApplicationRoles.PurchaseOrderApprover);
        var missingComment = await fixture.Service.ExecuteActionAsync(
            created.PurchaseOrder.Id,
            PurchaseOrderWorkflow.RejectAction,
            new PurchaseOrderActionRequest(),
            CancellationToken.None);
        var rejected = await fixture.Service.ExecuteActionAsync(
            created.PurchaseOrder.Id,
            PurchaseOrderWorkflow.RejectAction,
            new PurchaseOrderActionRequest { Comment = "Please correct the delivery address." },
            CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.PendingApproval, submitted.PurchaseOrder!.Status);
        Assert.Equal(PurchaseOrderOperationStatus.ValidationFailed, missingComment.Status);
        Assert.Equal(PurchaseOrderStatus.Draft, rejected.PurchaseOrder!.Status);
        Assert.Contains(
            rejected.PurchaseOrder.Workflow!.History,
            entry => entry.ActionCode == PurchaseOrderWorkflow.RejectAction &&
                     entry.Comment == "Please correct the delivery address.");
    }

    [Fact]
    public async Task ExecuteActionAsync_LazilyStartsWorkflowForLegacyDraft()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        var workflowEngine = new WorkflowEngine(fixture.DbContext);
        await workflowEngine.DeleteInstanceAsync(
            PurchaseOrderWorkflow.EntityType,
            created.PurchaseOrder!.Id,
            CancellationToken.None);

        var submitted = await fixture.Service.ExecuteActionAsync(
            created.PurchaseOrder.Id,
            PurchaseOrderWorkflow.SubmitAction,
            new PurchaseOrderActionRequest(),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.Success, submitted.Status);
        Assert.Equal(PurchaseOrderStatus.PendingApproval, submitted.PurchaseOrder!.Status);
        Assert.Equal(PurchaseOrderWorkflow.PendingApprovalStep, submitted.PurchaseOrder.Workflow!.CurrentStepCode);
    }

    [Fact]
    public async Task CreateAsync_CopiesSelectedQuotationIntoDraftSnapshot()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.Success, result.Status);
        Assert.NotNull(result.PurchaseOrder);
        Assert.StartsWith("PO-", result.PurchaseOrder.PurchaseOrderNumber);
        Assert.Equal(PurchaseOrderStatus.Draft, result.PurchaseOrder.Status);
        Assert.NotNull(result.PurchaseOrder.Workflow);
        Assert.Equal(PurchaseOrderWorkflow.DraftStep, result.PurchaseOrder.Workflow.CurrentStepCode);
        Assert.Equal("QT-0001", result.PurchaseOrder.QuotationNumber);
        Assert.Equal("PR-0001", result.PurchaseOrder.PurchaseRequestNumber);
        Assert.Equal("SUP-0001", result.PurchaseOrder.SupplierCode);
        Assert.Equal("Supplier One", result.PurchaseOrder.SupplierName);
        Assert.Equal("Demo Admin", result.PurchaseOrder.CreatedByName);
        Assert.Equal(200m, result.PurchaseOrder.TotalAmount);
        var item = Assert.Single(result.PurchaseOrder.Items);
        Assert.Equal("ITEM-0001", item.ProductCode);
        Assert.Equal(2m, item.Quantity);
        Assert.Equal(100m, item.UnitPrice);

        fixture.Supplier.Name = "Supplier Renamed";
        fixture.Product.Name = "Product Renamed";
        await fixture.DbContext.SaveChangesAsync();
        var snapshot = await fixture.Service.GetByIdAsync(
            result.PurchaseOrder.Id,
            CancellationToken.None);

        Assert.Equal("Supplier One", snapshot!.SupplierName);
        Assert.Equal("Monitor", snapshot.Items[0].ProductName);
    }

    [Fact]
    public async Task CreateAsync_RejectsQuotationThatIsNotSelected()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        fixture.Quotation.Status = QuotationStatus.Submitted;
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.QuotationNotSelected, result.Status);
        Assert.Empty(fixture.DbContext.PurchaseOrders);
    }

    [Fact]
    public async Task CreateAsync_RejectsSecondOrderForSameQuotation()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        await fixture.Service.CreateAsync(fixture.ValidRequest(), CancellationToken.None);

        var duplicate = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.DuplicatePurchaseOrder, duplicate.Status);
        Assert.Single(fixture.DbContext.PurchaseOrders);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesDraftButRejectsIssuedOrder()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        var id = created.PurchaseOrder!.Id;
        var update = new UpdatePurchaseOrderRequest
        {
            OrderDate = fixture.Today,
            ExpectedDeliveryDate = fixture.Today.AddDays(21),
            DeliveryAddress = "  Updated warehouse  ",
            Notes = "  Handle with care.  "
        };

        var updated = await fixture.Service.UpdateAsync(id, update, CancellationToken.None);
        await fixture.ApproveAsync(id);
        await fixture.Service.IssueAsync(id, CancellationToken.None);
        var rejected = await fixture.Service.UpdateAsync(id, update, CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.Success, updated.Status);
        Assert.Equal("Updated warehouse", updated.PurchaseOrder!.DeliveryAddress);
        Assert.Equal("Handle with care.", updated.PurchaseOrder.Notes);
        Assert.Equal(PurchaseOrderOperationStatus.InvalidState, rejected.Status);
    }

    [Fact]
    public async Task IssueAsync_RequiresDeliveryDetailsAndRecordsIssuer()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var request = new CreatePurchaseOrderRequest
        {
            QuotationId = fixture.Quotation.Id,
            OrderDate = fixture.Today,
            ExpectedDeliveryDate = null,
            DeliveryAddress = null
        };
        var created = await fixture.Service.CreateAsync(request, CancellationToken.None);

        var rejected = await fixture.Service.ExecuteActionAsync(
            created.PurchaseOrder!.Id,
            PurchaseOrderWorkflow.SubmitAction,
            new PurchaseOrderActionRequest(),
            CancellationToken.None);
        await fixture.Service.UpdateAsync(
            created.PurchaseOrder.Id,
            new UpdatePurchaseOrderRequest
            {
                OrderDate = fixture.Today,
                ExpectedDeliveryDate = fixture.Today.AddDays(14),
                DeliveryAddress = "Main warehouse"
            },
            CancellationToken.None);
        await fixture.ApproveAsync(created.PurchaseOrder.Id);
        var issued = await fixture.Service.IssueAsync(
            created.PurchaseOrder.Id,
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.ValidationFailed, rejected.Status);
        Assert.Equal(PurchaseOrderOperationStatus.Success, issued.Status);
        Assert.Equal(PurchaseOrderStatus.Issued, issued.PurchaseOrder!.Status);
        Assert.Equal(4, issued.PurchaseOrder.IssuedByUserId);
        Assert.Equal("Demo Admin", issued.PurchaseOrder.IssuedByName);
        Assert.NotNull(issued.PurchaseOrder.IssuedAtUtc);
        Assert.NotNull(issued.PurchaseOrder.EmailDelivery);
        Assert.Equal(EmailDeliveryStatus.Pending, issued.PurchaseOrder.EmailDelivery.Status);
        Assert.Equal("orders@supplier.test", issued.PurchaseOrder.EmailDelivery.RecipientEmail);

        var email = await fixture.DbContext.EmailOutboxes.SingleAsync();
        Assert.Equal("PurchaseOrder", email.SourceType);
        Assert.Equal(issued.PurchaseOrder.Id, email.SourceId);
        Assert.Equal(issued.PurchaseOrder.PurchaseOrderNumber, email.SourceReference);
        Assert.Equal("purchasing@project1.test", email.FromAddress);
        Assert.Contains("PO-", email.Subject);
        Assert.Contains("Monitor", email.HtmlBody);
        Assert.Contains("Main warehouse", email.HtmlBody);
        Assert.Equal(EmailTemplateConstants.PurchaseOrderIssuedCode, email.TemplateCode);
        Assert.Equal(1, email.TemplateVersion);
        var attachment = await fixture.DbContext.EmailAttachments.SingleAsync();
        Assert.Equal($"Purchase-Order-{issued.PurchaseOrder.PurchaseOrderNumber}.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(attachment.Content.LongLength, attachment.FileSizeBytes);
        Assert.Equal(FakePurchaseOrderPdfGenerator.PdfContent, attachment.Content);
    }

    [Fact]
    public async Task IssueAsync_LeavesOrderInDraft_WhenPdfGenerationFails()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync(
            new FakePurchaseOrderPdfGenerator(
                new InvalidOperationException("PDF rendering failed")));
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        await fixture.ApproveAsync(created.PurchaseOrder!.Id);

        var result = await fixture.Service.IssueAsync(
            created.PurchaseOrder.Id,
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.InvalidState, result.Status);
        Assert.Contains("PDF could not be generated", result.ErrorMessage);
        Assert.Equal(
            PurchaseOrderStatus.Approved,
            (await fixture.DbContext.PurchaseOrders.SingleAsync()).Status);
        Assert.Empty(fixture.DbContext.EmailOutboxes);
        Assert.Empty(fixture.DbContext.EmailAttachments);
    }

    [Fact]
    public async Task IssueAsync_RequiresAnActiveEmailTemplate()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        fixture.DbContext.EmailTemplates.RemoveRange(fixture.DbContext.EmailTemplates);
        await fixture.DbContext.SaveChangesAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        await fixture.ApproveAsync(created.PurchaseOrder!.Id);

        var result = await fixture.Service.IssueAsync(
            created.PurchaseOrder.Id,
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.InvalidState, result.Status);
        Assert.Contains("active Purchase Order email template", result.ErrorMessage);
        Assert.Equal(
            PurchaseOrderStatus.Approved,
            (await fixture.DbContext.PurchaseOrders.SingleAsync()).Status);
        Assert.Empty(fixture.DbContext.EmailOutboxes);
    }

    [Fact]
    public async Task IssueAsync_RequiresAValidSupplierEmail()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        fixture.Supplier.Email = null;
        await fixture.DbContext.SaveChangesAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        await fixture.ApproveAsync(created.PurchaseOrder!.Id);

        var result = await fixture.Service.IssueAsync(
            created.PurchaseOrder.Id,
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.ValidationFailed, result.Status);
        Assert.Contains("valid email", result.ErrorMessage);
        Assert.Empty(fixture.DbContext.EmailOutboxes);
    }

    [Fact]
    public async Task EmailOutboxProcessor_MarksSuccessfulDeliveryAsSent()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        await fixture.ApproveAsync(created.PurchaseOrder!.Id);
        await fixture.Service.IssueAsync(created.PurchaseOrder!.Id, CancellationToken.None);
        var sender = new FakeEmailSender();
        var processor = new EmailOutboxProcessor(
            fixture.DbContext,
            sender,
            NullLogger<EmailOutboxProcessor>.Instance);

        var processed = await processor.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        var email = await fixture.DbContext.EmailOutboxes.SingleAsync();
        Assert.Equal(EmailDeliveryStatus.Sent, email.Status);
        Assert.Equal(1, email.AttemptCount);
        Assert.NotNull(email.SentAtUtc);
        var message = Assert.Single(sender.Messages);
        Assert.Equal("orders@supplier.test", message.RecipientEmail);
        Assert.Equal("purchasing@project1.test", message.FromAddress);
        var messageAttachment = Assert.Single(message.Attachments!);
        Assert.Equal("application/pdf", messageAttachment.ContentType);
        Assert.Equal(FakePurchaseOrderPdfGenerator.PdfContent, messageAttachment.Content);
    }

    [Fact]
    public async Task FailedEmail_CanBeQueuedAndSentAgain()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        await fixture.ApproveAsync(created.PurchaseOrder!.Id);
        await fixture.Service.IssueAsync(created.PurchaseOrder!.Id, CancellationToken.None);
        var failingSender = new FakeEmailSender(new InvalidOperationException("SMTP unavailable"));
        var failingProcessor = new EmailOutboxProcessor(
            fixture.DbContext,
            failingSender,
            NullLogger<EmailOutboxProcessor>.Instance);
        await failingProcessor.ProcessNextAsync(CancellationToken.None);

        var failed = await fixture.Service.GetByIdAsync(
            created.PurchaseOrder.Id,
            CancellationToken.None);
        Assert.Equal(EmailDeliveryStatus.Failed, failed!.EmailDelivery!.Status);
        Assert.Contains("SMTP unavailable", failed.EmailDelivery.LastError);

        var retried = await fixture.Service.RetryEmailAsync(
            created.PurchaseOrder.Id,
            CancellationToken.None);
        Assert.Equal(EmailDeliveryStatus.Pending, retried.PurchaseOrder!.EmailDelivery!.Status);

        var successfulSender = new FakeEmailSender();
        var successfulProcessor = new EmailOutboxProcessor(
            fixture.DbContext,
            successfulSender,
            NullLogger<EmailOutboxProcessor>.Instance);
        await successfulProcessor.ProcessNextAsync(CancellationToken.None);

        var sent = await fixture.Service.GetByIdAsync(
            created.PurchaseOrder.Id,
            CancellationToken.None);
        Assert.Equal(EmailDeliveryStatus.Sent, sent!.EmailDelivery!.Status);
        Assert.Equal(2, sent.EmailDelivery.AttemptCount);
    }

    [Fact]
    public async Task CancelAsync_OnlyCancelsIssuedOrderWithReason()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        var id = created.PurchaseOrder!.Id;

        var draftResult = await fixture.Service.CancelAsync(
            id,
            new CancelPurchaseOrderRequest { Reason = "Supplier unavailable." },
            CancellationToken.None);
        await fixture.ApproveAsync(id);
        await fixture.Service.IssueAsync(id, CancellationToken.None);
        var missingReason = await fixture.Service.CancelAsync(
            id,
            new CancelPurchaseOrderRequest(),
            CancellationToken.None);
        var cancelled = await fixture.Service.CancelAsync(
            id,
            new CancelPurchaseOrderRequest { Reason = "  Supplier unavailable.  " },
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.InvalidState, draftResult.Status);
        Assert.Equal(PurchaseOrderOperationStatus.ValidationFailed, missingReason.Status);
        Assert.Equal(PurchaseOrderOperationStatus.Success, cancelled.Status);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.PurchaseOrder!.Status);
        Assert.Equal("Supplier unavailable.", cancelled.PurchaseOrder.CancellationReason);
        Assert.Equal("Demo Admin", cancelled.PurchaseOrder.CancelledByName);
    }

    [Fact]
    public async Task DeleteAsync_AllowsDraftButRejectsIssuedOrder()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        var deleted = await fixture.Service.DeleteAsync(
            first.PurchaseOrder!.Id,
            CancellationToken.None);
        var second = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        await fixture.ApproveAsync(second.PurchaseOrder!.Id);
        await fixture.Service.IssueAsync(second.PurchaseOrder!.Id, CancellationToken.None);
        var rejected = await fixture.Service.DeleteAsync(
            second.PurchaseOrder.Id,
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.Success, deleted.Status);
        Assert.Equal(PurchaseOrderOperationStatus.InvalidState, rejected.Status);
        Assert.Single(fixture.DbContext.PurchaseOrders);
    }

    private sealed class PurchaseOrderFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private PurchaseOrderFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            Supplier supplier,
            Product product,
            Quotation quotation,
            IPurchaseOrderPdfGenerator pdfGenerator)
        {
            this.connection = connection;
            DbContext = dbContext;
            Supplier = supplier;
            Product = product;
            Quotation = quotation;
            CurrentUser = new FakeCurrentUserContext();
            Service = new PurchaseOrderService(
                dbContext,
                new WorkflowEngine(dbContext),
                CurrentUser,
                new EmailTemplateRenderer(dbContext),
                pdfGenerator,
                Options.Create(new SmtpOptions
                {
                    FromAddress = "purchasing@project1.test",
                    FromName = "Project1 Purchasing"
                }));
        }

        public AppDbContext DbContext { get; }

        public PurchaseOrderService Service { get; }

        public FakeCurrentUserContext CurrentUser { get; }

        public Supplier Supplier { get; }

        public Product Product { get; }

        public Quotation Quotation { get; }

        public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.UtcNow);

        public static async Task<PurchaseOrderFixture> CreateAsync(
            IPurchaseOrderPdfGenerator? pdfGenerator = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            dbContext.EmailTemplates.Add(new EmailTemplate
            {
                Code = EmailTemplateConstants.PurchaseOrderIssuedCode,
                Name = EmailTemplateConstants.PurchaseOrderIssuedName,
                Version = 1,
                Status = EmailTemplateStatus.Active,
                SubjectTemplate = DefaultEmailTemplates.PurchaseOrderSubject,
                HtmlBodyTemplate = DefaultEmailTemplates.PurchaseOrderHtmlBody,
                ToRule = EmailTemplateConstants.SupplierEmailRule,
                CreatedByName = "System",
                PublishedByName = "System",
                PublishedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.WorkflowProcessTemplates.Add(CreatePurchaseOrderWorkflowTemplate());

            var category = new ProductCategory { Code = "CAT-TEST", Name = "Test Category" };
            var unit = new UnitOfMeasure { Code = "UNIT", Name = "Unit" };
            var product = new Product
            {
                Code = "ITEM-0001",
                Name = "Monitor",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 120m
            };
            var supplier = new Supplier
            {
                Code = "SUP-0001",
                Name = "Supplier One",
                Email = "orders@supplier.test"
            };
            var purchaseRequestItem = new PurchaseRequestItem
            {
                Product = product,
                Quantity = 2m,
                EstimatedUnitPrice = 120m
            };
            var purchaseRequest = new PurchaseRequest
            {
                RequestNumber = "PR-0001",
                RequesterName = "Demo Requester",
                Items = [purchaseRequestItem]
            };
            var quotation = new Quotation
            {
                QuotationNumber = "QT-0001",
                PurchaseRequest = purchaseRequest,
                Supplier = supplier,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                SupplierQuotationReference = "SUP-Q-001",
                QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                Status = QuotationStatus.Selected,
                CreatedByUserId = 4,
                CreatedByName = "Demo Admin",
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                SelectedAtUtc = DateTimeOffset.UtcNow,
                Items =
                [
                    new QuotationItem
                    {
                        PurchaseRequestItem = purchaseRequestItem,
                        Product = product,
                        ProductCode = product.Code,
                        ProductName = product.Name,
                        UnitOfMeasureCode = unit.Code,
                        Quantity = 2m,
                        UnitPrice = 100m
                    }
                ]
            };

            dbContext.Quotations.Add(quotation);
            await dbContext.SaveChangesAsync();

            return new PurchaseOrderFixture(
                connection,
                dbContext,
                supplier,
                product,
                quotation,
                pdfGenerator ?? new FakePurchaseOrderPdfGenerator());
        }

        public CreatePurchaseOrderRequest ValidRequest() => new()
        {
            QuotationId = Quotation.Id,
            OrderDate = Today,
            ExpectedDeliveryDate = Today.AddDays(14),
            DeliveryAddress = "Main warehouse",
            Notes = "Deliver during office hours."
        };

        public async Task ApproveAsync(int id)
        {
            var submitted = await Service.ExecuteActionAsync(
                id,
                PurchaseOrderWorkflow.SubmitAction,
                new PurchaseOrderActionRequest(),
                CancellationToken.None);
            Assert.Equal(PurchaseOrderOperationStatus.Success, submitted.Status);
            Assert.Equal(PurchaseOrderStatus.PendingApproval, submitted.PurchaseOrder!.Status);

            var approved = await Service.ExecuteActionAsync(
                id,
                PurchaseOrderWorkflow.ApproveAction,
                new PurchaseOrderActionRequest(),
                CancellationToken.None);
            Assert.Equal(PurchaseOrderOperationStatus.Success, approved.Status);
            Assert.Equal(PurchaseOrderStatus.Approved, approved.PurchaseOrder!.Status);
        }

        private static WorkflowProcessTemplate CreatePurchaseOrderWorkflowTemplate()
        {
            var draft = new WorkflowStepTemplate
            {
                Code = PurchaseOrderWorkflow.DraftStep,
                Name = "Draft",
                DisplayOrder = 1,
                IsInitial = true
            };
            var pending = new WorkflowStepTemplate
            {
                Code = PurchaseOrderWorkflow.PendingApprovalStep,
                Name = "Pending Approval",
                DisplayOrder = 2
            };
            var approved = new WorkflowStepTemplate
            {
                Code = PurchaseOrderWorkflow.ApprovedStep,
                Name = "Approved",
                DisplayOrder = 3,
                IsTerminal = true
            };
            draft.Actions.Add(new WorkflowActionTemplate
            {
                Code = PurchaseOrderWorkflow.SubmitAction,
                Name = "Submit for approval",
                ToStepTemplate = pending,
                Actioners =
                [
                    new WorkflowActionerTemplate
                    {
                        ActionerType = WorkflowActionerType.Requester
                    }
                ]
            });
            pending.Actions.Add(new WorkflowActionTemplate
            {
                Code = PurchaseOrderWorkflow.ApproveAction,
                Name = "Approve purchase order",
                ToStepTemplate = approved,
                Actioners =
                [
                    new WorkflowActionerTemplate
                    {
                        ActionerType = WorkflowActionerType.Role,
                        ActionerKey = ApplicationRoles.PurchaseOrderApprover
                    }
                ]
            });
            pending.Actions.Add(new WorkflowActionTemplate
            {
                Code = PurchaseOrderWorkflow.RejectAction,
                Name = "Reject purchase order",
                ToStepTemplate = draft,
                RequiresComment = true,
                Actioners =
                [
                    new WorkflowActionerTemplate
                    {
                        ActionerType = WorkflowActionerType.Role,
                        ActionerKey = ApplicationRoles.PurchaseOrderApprover
                    }
                ]
            });

            return new WorkflowProcessTemplate
            {
                Code = PurchaseOrderWorkflow.TemplateCode,
                Name = PurchaseOrderWorkflow.TemplateName,
                EntityType = PurchaseOrderWorkflow.EntityType,
                Version = 1,
                IsPublished = true,
                IsActive = true,
                PublishedAtUtc = DateTimeOffset.UtcNow,
                Steps = [draft, pending, approved]
            };
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public int UserId { get; private set; } = 4;

        public string DisplayName { get; private set; } = "Demo Admin";

        public int? DepartmentId => 1;

        public IReadOnlyCollection<string> Roles { get; private set; } = [ApplicationRoles.Admin];

        public bool IsInRole(string role) =>
            Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

        public void Set(int userId, string displayName, params string[] roles)
        {
            UserId = userId;
            DisplayName = displayName;
            Roles = roles;
        }
    }

    private sealed class FakeEmailSender(Exception? exception = null) : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePurchaseOrderPdfGenerator(Exception? exception = null)
        : IPurchaseOrderPdfGenerator
    {
        public static readonly byte[] PdfContent = "%PDF-1.7 test snapshot"u8.ToArray();

        public byte[] Generate(
            PurchaseOrder purchaseOrder,
            string issuedByName,
            DateTimeOffset issuedAtUtc)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return PdfContent.ToArray();
        }
    }
}
