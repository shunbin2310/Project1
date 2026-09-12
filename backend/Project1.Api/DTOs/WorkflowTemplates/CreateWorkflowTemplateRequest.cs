using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.WorkflowTemplates;

public sealed class CreateWorkflowTemplateRequest
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string EntityType { get; init; } = string.Empty;

    [Required]
    [MinLength(2)]
    public IReadOnlyList<WorkflowStepDefinitionRequest> Steps { get; init; } = [];
}
