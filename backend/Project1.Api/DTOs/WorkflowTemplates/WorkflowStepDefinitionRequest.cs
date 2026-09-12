using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.WorkflowTemplates;

public sealed class WorkflowStepDefinitionRequest
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int DisplayOrder { get; init; }

    public bool IsInitial { get; init; }

    public bool IsTerminal { get; init; }

    public IReadOnlyList<WorkflowActionDefinitionRequest> Actions { get; init; } = [];
}
