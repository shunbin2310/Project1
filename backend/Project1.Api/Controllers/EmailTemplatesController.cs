using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.EmailTemplates;
using Project1.Api.Services.EmailTemplates;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/email-templates")]
public sealed class EmailTemplatesController(IEmailTemplateService emailTemplateService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EmailTemplateSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EmailTemplateSummaryResponse>>> GetAll(
        CancellationToken cancellationToken) =>
        Ok(await emailTemplateService.GetAllAsync(cancellationToken));

    [HttpGet("{id:int}", Name = "GetEmailTemplateById")]
    [ProducesResponseType<EmailTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmailTemplateResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var template = await emailTemplateService.GetByIdAsync(id, cancellationToken);
        return template is null ? NotFound() : Ok(template);
    }

    [HttpPost("{id:int}/versions")]
    [ProducesResponseType<EmailTemplateResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<EmailTemplateResponse>> CreateVersion(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await emailTemplateService.CreateVersionAsync(id, cancellationToken);
        return result.Status == EmailTemplateOperationStatus.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Template!.Id }, result.Template)
            : OperationProblem(result);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<EmailTemplateResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailTemplateResponse>> Update(
        int id,
        UpdateEmailTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await emailTemplateService.UpdateAsync(id, request, cancellationToken);
        return result.Status == EmailTemplateOperationStatus.Success
            ? Ok(result.Template)
            : OperationProblem(result);
    }

    [HttpPost("preview")]
    [ProducesResponseType<EmailTemplatePreviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailTemplatePreviewResponse>> Preview(
        PreviewEmailTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await emailTemplateService.PreviewAsync(request, cancellationToken);
        return result.Status == EmailTemplateOperationStatus.Success
            ? Ok(result.Preview)
            : OperationProblem(result);
    }

    [HttpPost("{id:int}/publish")]
    [ProducesResponseType<EmailTemplateResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailTemplateResponse>> Publish(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await emailTemplateService.PublishAsync(id, cancellationToken);
        return result.Status == EmailTemplateOperationStatus.Success
            ? Ok(result.Template)
            : OperationProblem(result);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await emailTemplateService.DeleteAsync(id, cancellationToken);
        return result.Status == EmailTemplateOperationStatus.Success
            ? NoContent()
            : OperationProblem(result);
    }

    private ObjectResult OperationProblem(EmailTemplateOperationResult result)
    {
        if (result.Status == EmailTemplateOperationStatus.NotFound)
        {
            return StatusCode(StatusCodes.Status404NotFound, new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Email template was not found."
            });
        }

        var statusCode = result.Status == EmailTemplateOperationStatus.ValidationFailed
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status409Conflict;
        return StatusCode(statusCode, new ProblemDetails
        {
            Status = statusCode,
            Title = result.Status switch
            {
                EmailTemplateOperationStatus.ValidationFailed => "Email template validation failed.",
                EmailTemplateOperationStatus.InvalidState => "Email template state does not allow this operation.",
                _ => "Email template operation conflicts with existing data."
            },
            Detail = result.ErrorMessage
        });
    }
}
