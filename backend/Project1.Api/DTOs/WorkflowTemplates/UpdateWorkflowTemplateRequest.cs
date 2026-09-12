using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.WorkflowTemplates;

public sealed record UpdateWorkflowTemplateRequest
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [MinLength(2)]
    public IReadOnlyList<WorkflowStepDefinitionRequest> Steps { get; init; } = [];
}
