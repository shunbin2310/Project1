using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Workflows;
using Project1.Api.Entities.Workflows;

namespace Project1.Api.Services.Workflows;

public sealed class WorkflowEngine(AppDbContext dbContext) : IWorkflowEngine
{
    public async Task<WorkflowExecutionResult> StartAsync(
        string entityType,
        int entityId,
        string? requesterName,
        CancellationToken cancellationToken)
    {
        var normalizedEntityType = entityType.Trim();
        var existing = await dbContext.WorkflowProcessInstances.AnyAsync(
            instance => instance.EntityType == normalizedEntityType && instance.EntityId == entityId,
            cancellationToken);

        if (existing)
        {
            return new WorkflowExecutionResult(
                WorkflowExecutionStatus.ActionNotAvailable,
                ErrorMessage: "A workflow instance already exists for this record.");
        }

        var template = await dbContext.WorkflowProcessTemplates
            .AsSplitQuery()
            .Include(process => process.Steps)
                .ThenInclude(step => step.Actions)
                    .ThenInclude(action => action.Actioners)
            .Where(process =>
                process.EntityType == normalizedEntityType &&
                process.IsPublished &&
                process.IsActive)
            .OrderByDescending(process => process.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (template is null)
        {
            return new WorkflowExecutionResult(
                WorkflowExecutionStatus.TemplateNotFound,
                ErrorMessage: $"No active published workflow template exists for '{normalizedEntityType}'.");
        }

        var initialSteps = template.Steps.Where(step => step.IsInitial).ToList();
        if (initialSteps.Count != 1)
        {
            return new WorkflowExecutionResult(
                WorkflowExecutionStatus.TemplateNotFound,
                ErrorMessage: "The workflow template must contain exactly one initial step.");
        }

        var instance = new WorkflowProcessInstance
        {
            ProcessTemplateId = template.Id,
            TemplateCode = template.Code,
            TemplateName = template.Name,
            TemplateVersion = template.Version,
            EntityType = normalizedEntityType,
            EntityId = entityId
        };

        var stepMap = template.Steps.ToDictionary(
            step => step.Id,
            step => new WorkflowStepInstance
            {
                SourceStepTemplateId = step.Id,
                Code = step.Code,
                Name = step.Name,
                DisplayOrder = step.DisplayOrder,
                IsInitial = step.IsInitial,
                IsTerminal = step.IsTerminal
            });

        instance.Steps = stepMap.Values.ToList();

        foreach (var stepTemplate in template.Steps)
        {
            var fromStep = stepMap[stepTemplate.Id];

            foreach (var actionTemplate in stepTemplate.Actions)
            {
                fromStep.Actions.Add(new WorkflowActionInstance
                {
                    SourceActionTemplateId = actionTemplate.Id,
                    Code = actionTemplate.Code,
                    Name = actionTemplate.Name,
                    RequiresComment = actionTemplate.RequiresComment,
                    ToStepInstance = stepMap[actionTemplate.ToStepTemplateId],
                    Actioners = actionTemplate.Actioners.Select(actioner =>
                        new WorkflowActionerInstance
                        {
                            ActionerType = actioner.ActionerType,
                            ActionerKey = actioner.ActionerType == WorkflowActionerType.Requester
                                ? NormalizeOptional(requesterName) ?? string.Empty
                                : actioner.ActionerKey ?? string.Empty
                        }).ToList()
                });
            }
        }

        var initialStep = stepMap[initialSteps[0].Id];
        instance.History.Add(new WorkflowHistory
        {
            ToStepCode = initialStep.Code,
            ActionCode = "START",
            ActionBy = NormalizeOptional(requesterName) ?? "System",
            Comment = $"Workflow instance created from {template.Code} version {template.Version}."
        });

        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        dbContext.WorkflowProcessInstances.Add(instance);
        await dbContext.SaveChangesAsync(cancellationToken);

        instance.CurrentStepInstanceId = initialStep.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return await SuccessResultAsync(normalizedEntityType, entityId, cancellationToken);
    }

    public async Task<WorkflowExecutionResult> ExecuteActionAsync(
        string entityType,
        int entityId,
        string actionCode,
        WorkflowActor actor,
        string? comment,
        CancellationToken cancellationToken)
    {
        var instance = await InstanceQuery(tracking: true)
            .SingleOrDefaultAsync(
                item => item.EntityType == entityType && item.EntityId == entityId,
                cancellationToken);

        if (instance is null)
        {
            return new WorkflowExecutionResult(
                WorkflowExecutionStatus.InstanceNotFound,
                ErrorMessage: "Workflow instance was not found.");
        }

        if (instance.Status == WorkflowInstanceStatus.Completed)
        {
            return ActionNotAvailable("The workflow has already completed.");
        }

        var currentStep = instance.Steps.Single(step => step.Id == instance.CurrentStepInstanceId);
        var normalizedActionCode = actionCode.Trim().ToUpperInvariant();
        var action = currentStep.Actions.SingleOrDefault(item => item.Code == normalizedActionCode);

        if (action is null)
        {
            return ActionNotAvailable(
                $"Action '{normalizedActionCode}' is not available from step '{currentStep.Code}'.");
        }

        if (action.RequiresComment && string.IsNullOrWhiteSpace(comment))
        {
            return new WorkflowExecutionResult(
                WorkflowExecutionStatus.CommentRequired,
                ErrorMessage: $"A comment is required for action '{normalizedActionCode}'.");
        }

        var actorName = actor.Name.Trim();
        if (actorName.Length < 2 || !IsAuthorized(action.Actioners, actorName, actor.Roles))
        {
            return new WorkflowExecutionResult(
                WorkflowExecutionStatus.Unauthorized,
                ErrorMessage: $"'{actorName}' is not authorized to execute '{normalizedActionCode}'.");
        }

        var nextStep = action.ToStepInstance;
        var actionAtUtc = DateTimeOffset.UtcNow;

        instance.CurrentStepInstanceId = nextStep.Id;

        if (nextStep.IsTerminal)
        {
            instance.Status = WorkflowInstanceStatus.Completed;
            instance.CompletedAtUtc = actionAtUtc;
        }

        instance.History.Add(new WorkflowHistory
        {
            ActionInstanceId = action.Id,
            FromStepCode = currentStep.Code,
            ToStepCode = nextStep.Code,
            ActionCode = action.Code,
            ActionBy = actorName,
            Comment = NormalizeOptional(comment),
            ActionAtUtc = actionAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return await SuccessResultAsync(entityType, entityId, cancellationToken);
    }

    public async Task<WorkflowInstanceResponse?> GetInstanceAsync(
        string entityType,
        int entityId,
        CancellationToken cancellationToken)
    {
        var instance = await InstanceQuery(tracking: false)
            .SingleOrDefaultAsync(
                item => item.EntityType == entityType && item.EntityId == entityId,
                cancellationToken);

        return instance is null ? null : ToResponse(instance);
    }

    public async Task<IReadOnlyDictionary<int, WorkflowInstanceResponse>> GetInstancesAsync(
        string entityType,
        IReadOnlyCollection<int> entityIds,
        CancellationToken cancellationToken)
    {
        var instances = await InstanceQuery(tracking: false)
            .Where(instance =>
                instance.EntityType == entityType && entityIds.Contains(instance.EntityId))
            .ToListAsync(cancellationToken);

        return instances.ToDictionary(instance => instance.EntityId, ToResponse);
    }

    public async Task UpdateRequesterAsync(
        string entityType,
        int entityId,
        string? requesterName,
        CancellationToken cancellationToken)
    {
        var actioners = await dbContext.WorkflowActionerInstances
            .Where(actioner =>
                actioner.ActionerType == WorkflowActionerType.Requester &&
                actioner.ActionInstance.FromStepInstance.ProcessInstance.EntityType == entityType &&
                actioner.ActionInstance.FromStepInstance.ProcessInstance.EntityId == entityId)
            .ToListAsync(cancellationToken);
        var resolvedName = NormalizeOptional(requesterName) ?? string.Empty;

        foreach (var actioner in actioners)
        {
            actioner.ActionerKey = resolvedName;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteInstanceAsync(
        string entityType,
        int entityId,
        CancellationToken cancellationToken)
    {
        var instance = await InstanceQuery(tracking: true)
            .SingleOrDefaultAsync(
                item => item.EntityType == entityType && item.EntityId == entityId,
                cancellationToken);

        if (instance is null)
        {
            return false;
        }

        var actions = instance.Steps.SelectMany(step => step.Actions).ToList();
        dbContext.WorkflowActionerInstances.RemoveRange(actions.SelectMany(action => action.Actioners));
        dbContext.WorkflowActionInstances.RemoveRange(actions);
        dbContext.WorkflowHistory.RemoveRange(instance.History);
        dbContext.WorkflowStepInstances.RemoveRange(instance.Steps);
        dbContext.WorkflowProcessInstances.Remove(instance);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private IQueryable<WorkflowProcessInstance> InstanceQuery(bool tracking)
    {
        var query = dbContext.WorkflowProcessInstances
            .AsSplitQuery()
            .Include(instance => instance.Steps)
                .ThenInclude(step => step.Actions)
                    .ThenInclude(action => action.Actioners)
            .Include(instance => instance.Steps)
                .ThenInclude(step => step.Actions)
                    .ThenInclude(action => action.ToStepInstance)
            .Include(instance => instance.History)
            .AsQueryable();

        return tracking ? query : query.AsNoTracking();
    }

    private async Task<WorkflowExecutionResult> SuccessResultAsync(
        string entityType,
        int entityId,
        CancellationToken cancellationToken)
    {
        var workflow = await GetInstanceAsync(entityType, entityId, cancellationToken);
        return new WorkflowExecutionResult(WorkflowExecutionStatus.Success, workflow);
    }

    private static bool IsAuthorized(
        IEnumerable<WorkflowActionerInstance> actioners,
        string actorName,
        IReadOnlyCollection<string> actorRoles)
    {
        var roles = actorRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return actioners.Any(actioner => actioner.ActionerType switch
        {
            WorkflowActionerType.Requester or WorkflowActionerType.User =>
                string.Equals(actioner.ActionerKey, actorName, StringComparison.OrdinalIgnoreCase),
            WorkflowActionerType.Role => roles.Contains(actioner.ActionerKey),
            _ => false
        });
    }

    private static WorkflowInstanceResponse ToResponse(WorkflowProcessInstance instance)
    {
        var currentStep = instance.Steps.Single(step => step.Id == instance.CurrentStepInstanceId);
        var actions = currentStep.Actions
            .OrderBy(action => action.Id)
            .Select(action => new WorkflowAvailableActionResponse(
                action.Code,
                action.Name,
                action.RequiresComment,
                action.ToStepInstance.Code,
                action.ToStepInstance.Name,
                action.Actioners.Select(actioner => new WorkflowActionerResponse(
                    actioner.ActionerType,
                    actioner.ActionerKey)).ToList()))
            .ToList();
        var history = instance.History
            .OrderBy(entry => entry.ActionAtUtc)
            .Select(entry => new WorkflowHistoryResponse(
                entry.Id,
                entry.FromStepCode,
                entry.ToStepCode,
                entry.ActionCode,
                entry.ActionBy,
                entry.Comment,
                entry.ActionAtUtc))
            .ToList();

        return new WorkflowInstanceResponse(
            instance.Id,
            instance.TemplateCode,
            instance.TemplateName,
            instance.TemplateVersion,
            instance.EntityType,
            instance.EntityId,
            instance.Status,
            currentStep.Code,
            currentStep.Name,
            instance.StartedAtUtc,
            instance.CompletedAtUtc,
            actions,
            history);
    }

    private static WorkflowExecutionResult ActionNotAvailable(string message) =>
        new(WorkflowExecutionStatus.ActionNotAvailable, ErrorMessage: message);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
