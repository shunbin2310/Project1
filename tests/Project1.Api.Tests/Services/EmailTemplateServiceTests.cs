using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.EmailTemplates;
using Project1.Api.Email;
using Project1.Api.Entities;
using Project1.Api.Services.Authentication;
using Project1.Api.Services.EmailTemplates;

namespace Project1.Api.Tests.Services;

public sealed class EmailTemplateServiceTests
{
    [Fact]
    public async Task CreateVersion_UpdatePreviewAndPublish_PreservesVersionHistory()
    {
        await using var fixture = await EmailTemplateFixture.CreateAsync();

        var created = await fixture.Service.CreateVersionAsync(
            fixture.ActiveTemplate.Id,
            CancellationToken.None);
        var draft = created.Template!;
        var request = new UpdateEmailTemplateRequest
        {
            Name = "Purchase Order Supplier Email",
            SubjectTemplate = "PO {{PurchaseOrderNumber}} for {{SupplierName}}",
            HtmlBodyTemplate = "<h1>{{PurchaseOrderNumber}}</h1>{{ItemsTable}}<p>{{TotalAmount}}</p>",
            CcRecipients = "finance@project1.test",
            BccRecipients = "audit@project1.test"
        };

        var updated = await fixture.Service.UpdateAsync(draft.Id, request, CancellationToken.None);
        var preview = await fixture.Service.PreviewAsync(
            new PreviewEmailTemplateRequest
            {
                SubjectTemplate = request.SubjectTemplate,
                HtmlBodyTemplate = request.HtmlBodyTemplate,
                CcRecipients = request.CcRecipients,
                BccRecipients = request.BccRecipients
            },
            CancellationToken.None);
        var published = await fixture.Service.PublishAsync(draft.Id, CancellationToken.None);

        Assert.Equal(EmailTemplateOperationStatus.Success, created.Status);
        Assert.Equal(2, draft.Version);
        Assert.Equal(EmailTemplateOperationStatus.Success, updated.Status);
        Assert.Equal("finance@project1.test", updated.Template!.CcRecipients);
        Assert.Equal(EmailTemplateOperationStatus.Success, preview.Status);
        Assert.Contains("PO-0001", preview.Preview!.Subject);
        Assert.Contains("USB-C Monitor", preview.Preview.HtmlBody);
        Assert.Equal(EmailTemplateOperationStatus.Success, published.Status);

        var versions = await fixture.DbContext.EmailTemplates
            .OrderBy(template => template.Version)
            .ToListAsync();
        Assert.Equal(EmailTemplateStatus.Superseded, versions[0].Status);
        Assert.Equal(EmailTemplateStatus.Active, versions[1].Status);
        Assert.Equal(4, versions[1].PublishedByUserId);
    }

    [Fact]
    public async Task UpdateAsync_RejectsUnknownPlaceholder()
    {
        await using var fixture = await EmailTemplateFixture.CreateAsync();
        var draft = (await fixture.Service.CreateVersionAsync(
            fixture.ActiveTemplate.Id,
            CancellationToken.None)).Template!;

        var result = await fixture.Service.UpdateAsync(
            draft.Id,
            new UpdateEmailTemplateRequest
            {
                Name = draft.Name,
                SubjectTemplate = "Purchase Order {{UnknownValue}}",
                HtmlBodyTemplate = draft.HtmlBodyTemplate
            },
            CancellationToken.None);

        Assert.Equal(EmailTemplateOperationStatus.ValidationFailed, result.Status);
        Assert.Contains("Unknown placeholder", result.ErrorMessage);
    }

    [Fact]
    public async Task ActiveTemplate_CannotBeUpdatedOrDeleted()
    {
        await using var fixture = await EmailTemplateFixture.CreateAsync();

        var update = await fixture.Service.UpdateAsync(
            fixture.ActiveTemplate.Id,
            new UpdateEmailTemplateRequest
            {
                Name = fixture.ActiveTemplate.Name,
                SubjectTemplate = fixture.ActiveTemplate.SubjectTemplate,
                HtmlBodyTemplate = fixture.ActiveTemplate.HtmlBodyTemplate
            },
            CancellationToken.None);
        var delete = await fixture.Service.DeleteAsync(
            fixture.ActiveTemplate.Id,
            CancellationToken.None);

        Assert.Equal(EmailTemplateOperationStatus.InvalidState, update.Status);
        Assert.Equal(EmailTemplateOperationStatus.InvalidState, delete.Status);
    }

    private sealed class EmailTemplateFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private EmailTemplateFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            EmailTemplate activeTemplate)
        {
            this.connection = connection;
            DbContext = dbContext;
            ActiveTemplate = activeTemplate;
            var renderer = new EmailTemplateRenderer(dbContext);
            Service = new EmailTemplateService(dbContext, new FakeCurrentUserContext(), renderer);
        }

        public AppDbContext DbContext { get; }

        public EmailTemplateService Service { get; }

        public EmailTemplate ActiveTemplate { get; }

        public static async Task<EmailTemplateFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var activeTemplate = new EmailTemplate
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
            };
            dbContext.EmailTemplates.Add(activeTemplate);
            await dbContext.SaveChangesAsync();

            return new EmailTemplateFixture(connection, dbContext, activeTemplate);
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

        public int UserId => 4;

        public string DisplayName => "Demo Admin";

        public int? DepartmentId => 1;

        public IReadOnlyCollection<string> Roles => [ApplicationRoles.Admin];

        public bool IsInRole(string role) =>
            string.Equals(role, ApplicationRoles.Admin, StringComparison.OrdinalIgnoreCase);
    }
}
