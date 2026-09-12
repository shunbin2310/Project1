using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.WorkflowTemplates;

public sealed class WorkflowActionDefinitionRequest
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string ToStepCode { get; init; } = string.Empty;

    public bool RequiresComment { get; init; }

    [Required]
    [MinLength(1)]
    public IReadOnlyList<WorkflowActionerDefinitionRequest> Actioners { get; init; } = [];
}
