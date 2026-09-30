using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.EmailRecords;
using Project1.Api.Entities;
using Project1.Api.Services.EmailRecords;

namespace Project1.Api.Tests.Controllers;

public sealed class EmailRecordsControllerTests
{
    [Fact]
    public async Task GetAll_ReturnsBadRequest_WhenDateRangeIsInvalid()
    {
        var controller = new EmailRecordsController(new FakeEmailRecordService());

        var response = await controller.GetAll(
            createdFrom: new DateOnly(2026, 9, 28),
            createdTo: new DateOnly(2026, 9, 27),
            cancellationToken: CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task Retry_ReturnsConflict_WhenRecordIsNotFailed()
    {
        var service = new FakeEmailRecordService
        {
            RetryResult = new EmailRecordOperationResult(
                EmailRecordOperationStatus.InvalidState,
                ErrorMessage: "Only a failed email can be retried.")
        };
        var controller = new EmailRecordsController(service);

        var response = await controller.Retry(1, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task Resend_ReturnsCreatedAtAction_WithNewRecord()
    {
        var responseRecord = CreateDetails(2);
        var service = new FakeEmailRecordService
        {
            ResendResult = new EmailRecordOperationResult(
                EmailRecordOperationStatus.Success,
                responseRecord)
        };
        var controller = new EmailRecordsController(service);

        var response = await controller.Resend(1, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        Assert.Equal(nameof(EmailRecordsController.GetById), created.ActionName);
        Assert.Equal(2, ((EmailRecordDetailsResponse)created.Value!).Id);
    }

    [Fact]
    public async Task ViewAttachment_ReturnsInlinePdf()
    {
        var service = new FakeEmailRecordService
        {
            AttachmentResult = new EmailAttachmentFileResult(
                "Purchase-Order-PO-0001.pdf",
                "application/pdf",
                "%PDF test"u8.ToArray())
        };
        var controller = new EmailRecordsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var response = await controller.ViewAttachment(1, 2, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(response);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("%PDF test"u8.ToArray(), file.FileContents);
        Assert.StartsWith("inline", controller.Response.Headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task DownloadAttachment_ReturnsPdfWithSavedFileName()
    {
        var service = new FakeEmailRecordService
        {
            AttachmentResult = new EmailAttachmentFileResult(
                "Purchase-Order-PO-0001.pdf",
                "application/pdf",
                "%PDF test"u8.ToArray())
        };
        var controller = new EmailRecordsController(service);

        var response = await controller.DownloadAttachment(1, 2, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(response);
        Assert.Equal("Purchase-Order-PO-0001.pdf", file.FileDownloadName);
        Assert.Equal("application/pdf", file.ContentType);
    }

    private static EmailRecordDetailsResponse CreateDetails(int id) => new(
        id,
        "PurchaseOrder",
        1,
        "PO-0001",
        "purchasing@project1.test",
        "Project1 Purchasing",
        "orders@supplier.test",
        null,
        null,
        "Purchase Order PO-0001",
        "<h1>PO-0001</h1>",
        "PURCHASE_ORDER_ISSUED",
        1,
        EmailDeliveryStatus.Pending,
        0,
        4,
        "Demo Admin",
        1,
        DateTimeOffset.UtcNow,
        null,
        null,
        null,
        null,
        []);

    private sealed class FakeEmailRecordService : IEmailRecordService
    {
        public EmailRecordOperationResult RetryResult { get; init; } =
            new(EmailRecordOperationStatus.Success, CreateDetails(1));

        public EmailRecordOperationResult ResendResult { get; init; } =
            new(EmailRecordOperationStatus.Success, CreateDetails(2));

        public EmailAttachmentFileResult? AttachmentResult { get; init; }

        public Task<IReadOnlyList<EmailRecordSummaryResponse>> GetAllAsync(
            string? search,
            EmailDeliveryStatus? status,
            string? sourceType,
            DateOnly? createdFrom,
            DateOnly? createdTo,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmailRecordSummaryResponse>>([]);

        public Task<EmailRecordDetailsResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<EmailRecordDetailsResponse?>(CreateDetails(id));

        public Task<EmailAttachmentFileResult?> GetAttachmentAsync(
            int emailRecordId,
            int attachmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(AttachmentResult);

        public Task<EmailRecordOperationResult> RetryAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(RetryResult);

        public Task<EmailRecordOperationResult> ResendAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(ResendResult);
    }
}
