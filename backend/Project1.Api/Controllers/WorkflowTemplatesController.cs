using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.WorkflowTemplates;
using Project1.Api.Services.WorkflowTemplates;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/workflow-templates")]
public sealed class WorkflowTemplatesController(
    IWorkflowTemplateService workflowTemplateService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WorkflowTemplateSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkflowTemplateSummaryResponse>>> GetAll(
        [FromQuery] string? code = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await workflowTemplateService.GetAllAsync(code, cancellationToken));
    }

    [HttpGet("{id:int}", Name = "GetWorkflowTemplateById")]
    [ProducesResponseType<WorkflowTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowTemplateResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var template = await workflowTemplateService.GetByIdAsync(id, cancellationToken);
        return template is null ? NotFound() : Ok(template);
    }

    [HttpPost]
    [ProducesResponseType<WorkflowTemplateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkflowTemplateResponse>> Create(
        CreateWorkflowTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await workflowTemplateService.CreateAsync(request, cancellationToken);
        return result.Status == WorkflowTemplateOperationStatus.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Template!.Id }, result.Template)
            : OperationProblem(result);
    }

    [HttpPost("{id:int}/versions")]
    [ProducesResponseType<WorkflowTemplateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkflowTemplateResponse>> CreateVersion(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await workflowTemplateService.CreateVersionAsync(id, cancellationToken);
        return result.Status == WorkflowTemplateOperationStatus.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Template!.Id }, result.Template)
            : OperationProblem(result);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<WorkflowTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkflowTemplateResponse>> Update(
        int id,
        UpdateWorkflowTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await workflowTemplateService.UpdateAsync(id, request, cancellationToken);
        return result.Status == WorkflowTemplateOperationStatus.Success
            ? Ok(result.Template)
            : OperationProblem(result);
    }

    [HttpPost("{id:int}/publish")]
    [ProducesResponseType<WorkflowTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkflowTemplateResponse>> Publish(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await workflowTemplateService.PublishAsync(id, cancellationToken);
        return result.Status == WorkflowTemplateOperationStatus.Success
            ? Ok(result.Template)
            : OperationProblem(result);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await workflowTemplateService.DeleteAsync(id, cancellationToken);
        return result.Status == WorkflowTemplateOperationStatus.Success
            ? NoContent()
            : OperationProblem(result).Result!;
    }

    private ActionResult<WorkflowTemplateResponse> OperationProblem(
        WorkflowTemplateOperationResult result)
    {
        if (result.Status == WorkflowTemplateOperationStatus.NotFound)
        {
            return NotFound();
        }

        var statusCode = result.Status == WorkflowTemplateOperationStatus.ValidationFailed
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status409Conflict;
        return new ObjectResult(new ProblemDetails
        {
            Status = statusCode,
            Title = result.Status switch
            {
                WorkflowTemplateOperationStatus.ValidationFailed =>
                    "Workflow template validation failed.",
                WorkflowTemplateOperationStatus.InvalidState =>
                    "Workflow template state does not allow this operation.",
                _ => "Workflow template operation conflicts with existing data."
            },
            Detail = result.ErrorMessage
        })
        {
            StatusCode = statusCode
        };
    }
}
