using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.Entities;

namespace Project1.Api.Email;

public static class EmailTemplateSeeder
{
    public static async Task SeedEmailTemplatesAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await dbContext.EmailTemplates.AnyAsync(
            template => template.Code == EmailTemplateConstants.PurchaseOrderIssuedCode))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
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
            CreatedAtUtc = now,
            PublishedByName = "System",
            PublishedAtUtc = now
        });

        await dbContext.SaveChangesAsync();
    }
}
