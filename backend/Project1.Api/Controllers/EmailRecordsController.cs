using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Project1.Api.Authentication;
using Project1.Api.DTOs.EmailRecords;
using Project1.Api.Entities;
using Project1.Api.Services.EmailRecords;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.AdminOrProcurement)]
[Route("api/email-records")]
public sealed class EmailRecordsController(IEmailRecordService emailRecordService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EmailRecordSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<EmailRecordSummaryResponse>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] EmailDeliveryStatus? status = null,
        [FromQuery] string? sourceType = null,
        [FromQuery] DateOnly? createdFrom = null,
        [FromQuery] DateOnly? createdTo = null,
        CancellationToken cancellationToken = default)
    {
        if (createdFrom.HasValue && createdTo.HasValue && createdFrom > createdTo)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Email record filters are invalid.",
                Detail = "Created from date cannot be after created to date."
            });
        }

        var records = await emailRecordService.GetAllAsync(
            search,
            status,
            sourceType,
            createdFrom,
            createdTo,
            cancellationToken);
        return Ok(records);
    }

    [HttpGet("{id:int}", Name = "GetEmailRecordById")]
    [ProducesResponseType<EmailRecordDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmailRecordDetailsResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var record = await emailRecordService.GetByIdAsync(id, cancellationToken);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpGet("{id:int}/attachments/{attachmentId:int}/view")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ViewAttachment(
        int id,
        int attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await emailRecordService.GetAttachmentAsync(
            id,
            attachmentId,
            cancellationToken);
        if (attachment is null)
        {
            return NotFound();
        }

        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
        {
            FileNameStar = attachment.FileName
        }.ToString();
        return File(attachment.Content, attachment.ContentType);
    }

    [HttpGet("{id:int}/attachments/{attachmentId:int}/download")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachment(
        int id,
        int attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await emailRecordService.GetAttachmentAsync(
            id,
            attachmentId,
            cancellationToken);
        return attachment is null
            ? NotFound()
            : File(attachment.Content, attachment.ContentType, attachment.FileName);
    }

    [HttpPost("{id:int}/retry")]
    [ProducesResponseType<EmailRecordDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmailRecordDetailsResponse>> Retry(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await emailRecordService.RetryAsync(id, cancellationToken);
        return OperationResponse(result);
    }

    [HttpPost("{id:int}/resend")]
    [ProducesResponseType<EmailRecordDetailsResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmailRecordDetailsResponse>> Resend(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await emailRecordService.ResendAsync(id, cancellationToken);
        if (result.Status != EmailRecordOperationStatus.Success)
        {
            return OperationProblem(result);
        }

        var record = result.EmailRecord!;
        return CreatedAtAction(nameof(GetById), new { id = record.Id }, record);
    }

    private ActionResult<EmailRecordDetailsResponse> OperationResponse(
        EmailRecordOperationResult result) =>
        result.Status == EmailRecordOperationStatus.Success
            ? Ok(result.EmailRecord)
            : OperationProblem(result);

    private ObjectResult OperationProblem(EmailRecordOperationResult result)
    {
        var (statusCode, title) = result.Status switch
        {
            EmailRecordOperationStatus.NotFound =>
                (StatusCodes.Status404NotFound, "Email record was not found."),
            EmailRecordOperationStatus.InvalidState =>
                (StatusCodes.Status409Conflict, "Email record operation is not allowed."),
            _ =>
                (StatusCodes.Status500InternalServerError, "Email record operation failed.")
        };

        return StatusCode(statusCode, new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = result.ErrorMessage
        });
    }
}
