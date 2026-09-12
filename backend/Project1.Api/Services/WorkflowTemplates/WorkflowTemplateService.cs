using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.WorkflowTemplates;
using Project1.Api.Entities.Workflows;

namespace Project1.Api.Services.WorkflowTemplates;

public sealed partial class WorkflowTemplateService(AppDbContext dbContext)
    : IWorkflowTemplateService
{
    private static readonly HashSet<string> ValidRoles =
        ApplicationRoles.All.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<WorkflowTemplateSummaryResponse>> GetAllAsync(
        string? code,
        CancellationToken cancellationToken)
    {
        var query = dbContext.WorkflowProcessTemplates.AsNoTracking();
        var normalizedCode = NormalizeOptionalCode(code);

        if (normalizedCode is not null)
        {
            query = query.Where(template => template.Code == normalizedCode);
        }

        return await query
            .OrderBy(template => template.Code)
            .ThenByDescending(template => template.Version)
            .Select(template => new WorkflowTemplateSummaryResponse(
                template.Id,
                template.Code,
                template.Name,
                template.EntityType,
                template.Version,
                template.IsPublished,
                template.IsActive,
                template.Steps.Count,
                template.CreatedAtUtc,
                template.PublishedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkflowTemplateResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var template = await TemplateQuery(tracking: false)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return template is null ? null : ToResponse(template);
    }

    public async Task<WorkflowTemplateOperationResult> CreateAsync(
        CreateWorkflowTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(request.Code);
        var entityType = request.EntityType.Trim();

        if (!CodePattern().IsMatch(code))
        {
            return ValidationFailed(
                "Template code must start with a letter and contain only letters, numbers, or underscores.");
        }

        if (!EntityTypePattern().IsMatch(entityType))
        {
            return ValidationFailed(
                "Entity type must start with a letter and contain only letters, numbers, dots, or underscores.");
        }

        if (await dbContext.WorkflowProcessTemplates.AnyAsync(
            template => template.Code == code,
            cancellationToken))
        {
            return Conflict($"Workflow template code '{code}' already exists.");
        }

        var validation = await ValidateDefinitionAsync(
            request.Name,
            request.Steps,
            cancellationToken);
        if (validation.Error is not null)
        {
            return ValidationFailed(validation.Error);
        }

        var template = new WorkflowProcessTemplate
        {
            Code = code,
            Name = validation.Definition!.Name,
            EntityType = entityType,
            Version = 1,
            IsPublished = false,
            IsActive = false,
            Steps = BuildSteps(validation.Definition.Steps)
        };

        dbContext.WorkflowProcessTemplates.Add(template);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Success(await GetRequiredResponseAsync(template.Id, cancellationToken));
    }

    public async Task<WorkflowTemplateOperationResult> CreateVersionAsync(
        int sourceTemplateId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var source = await TemplateQuery(tracking: false)
            .SingleOrDefaultAsync(template => template.Id == sourceTemplateId, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (!source.IsPublished || !source.IsActive)
        {
            return InvalidState("Only the active published template can be copied into a new version.");
        }

        if (await dbContext.WorkflowProcessTemplates.AnyAsync(
            template => template.Code == source.Code && !template.IsPublished,
            cancellationToken))
        {
            return Conflict($"Template '{source.Code}' already has an unpublished draft version.");
        }

        var definition = DefinitionFromTemplate(source);
        var validation = await ValidateDefinitionAsync(
            definition.Name,
            definition.Steps,
            cancellationToken,
            validateUserActioners: false);
        if (validation.Error is not null)
        {
            return ValidationFailed(validation.Error);
        }

        var nextVersion = await dbContext.WorkflowProcessTemplates
            .Where(template => template.Code == source.Code)
            .MaxAsync(template => template.Version, cancellationToken) + 1;
        var draft = new WorkflowProcessTemplate
        {
            Code = source.Code,
            Name = validation.Definition!.Name,
            EntityType = source.EntityType,
            Version = nextVersion,
            IsPublished = false,
            IsActive = false,
            Steps = BuildSteps(validation.Definition.Steps)
        };

        dbContext.WorkflowProcessTemplates.Add(draft);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Success(await GetRequiredResponseAsync(draft.Id, cancellationToken));
    }

    public async Task<WorkflowTemplateOperationResult> UpdateAsync(
        int id,
        UpdateWorkflowTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var template = await TemplateQuery(tracking: true)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.IsPublished)
        {
            return InvalidState("Published workflow templates cannot be edited. Create a new version first.");
        }

        if (await IsUsedByInstanceAsync(id, cancellationToken))
        {
            return InvalidState("A workflow template used by an instance cannot be edited.");
        }

        var validation = await ValidateDefinitionAsync(
            request.Name,
            request.Steps,
            cancellationToken);
        if (validation.Error is not null)
        {
            return ValidationFailed(validation.Error);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        var existingSteps = template.Steps.ToArray();
        var existingActions = existingSteps.SelectMany(step => step.Actions).ToArray();
        dbContext.WorkflowActionTemplates.RemoveRange(existingActions);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.WorkflowStepTemplates.RemoveRange(existingSteps);
        await dbContext.SaveChangesAsync(cancellationToken);

        template.Name = validation.Definition!.Name;
        template.Steps = BuildSteps(validation.Definition.Steps);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Success(await GetRequiredResponseAsync(id, cancellationToken));
    }

    public async Task<WorkflowTemplateOperationResult> PublishAsync(
        int id,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var template = await TemplateQuery(tracking: true)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.IsPublished)
        {
            return InvalidState("This workflow template version has already been published.");
        }

        var definition = DefinitionFromTemplate(template);
        var validation = await ValidateDefinitionAsync(
            definition.Name,
            definition.Steps,
            cancellationToken);
        if (validation.Error is not null)
        {
            return ValidationFailed(validation.Error);
        }

        var previousTemplates = await dbContext.WorkflowProcessTemplates
            .Where(item =>
                item.Id != id &&
                item.EntityType == template.EntityType &&
                item.IsPublished &&
                item.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var previousTemplate in previousTemplates)
        {
            previousTemplate.IsActive = false;
        }

        template.IsPublished = true;
        template.IsActive = true;
        template.PublishedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Success(await GetRequiredResponseAsync(id, cancellationToken));
    }

    public async Task<WorkflowTemplateOperationResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var template = await TemplateQuery(tracking: true)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.IsPublished)
        {
            return InvalidState("Published workflow template versions cannot be deleted.");
        }

        if (await IsUsedByInstanceAsync(id, cancellationToken))
        {
            return InvalidState("A workflow template used by an instance cannot be deleted.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        var existingSteps = template.Steps.ToArray();
        var existingActions = existingSteps.SelectMany(step => step.Actions).ToArray();
        dbContext.WorkflowActionTemplates.RemoveRange(existingActions);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.WorkflowStepTemplates.RemoveRange(existingSteps);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.WorkflowProcessTemplates.Remove(template);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new WorkflowTemplateOperationResult(WorkflowTemplateOperationStatus.Success);
    }

    private async Task<(NormalizedDefinition? Definition, string? Error)> ValidateDefinitionAsync(
        string name,
        IReadOnlyList<WorkflowStepDefinitionRequest>? requestedSteps,
        CancellationToken cancellationToken,
        bool validateUserActioners = true)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is < 2 or > 150)
        {
            return (null, "Template name must contain between 2 and 150 characters.");
        }

        var steps = requestedSteps ?? [];
        if (steps.Count < 2)
        {
            return (null, "A workflow template must contain at least two steps.");
        }

        var normalizedSteps = new List<NormalizedStep>(steps.Count);
        var stepCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var displayOrders = new HashSet<int>();

        foreach (var step in steps)
        {
            var stepCode = NormalizeCode(step.Code);
            var stepName = step.Name.Trim();

            if (!CodePattern().IsMatch(stepCode))
            {
                return (null, $"Step code '{step.Code}' is invalid.");
            }

            if (stepName.Length is < 2 or > 100)
            {
                return (null, $"Step '{stepCode}' name must contain between 2 and 100 characters.");
            }

            if (!stepCodes.Add(stepCode))
            {
                return (null, $"Step code '{stepCode}' is duplicated.");
            }

            if (step.DisplayOrder < 1 || !displayOrders.Add(step.DisplayOrder))
            {
                return (null, "Every step must have a unique positive display order.");
            }

            normalizedSteps.Add(new NormalizedStep(
                stepCode,
                stepName,
                step.DisplayOrder,
                step.IsInitial,
                step.IsTerminal,
                []));
        }

        var initialSteps = normalizedSteps.Where(step => step.IsInitial).ToArray();
        if (initialSteps.Length != 1)
        {
            return (null, "A workflow template must contain exactly one initial step.");
        }

        if (initialSteps[0].IsTerminal)
        {
            return (null, "The initial step cannot also be a terminal step.");
        }

        if (!normalizedSteps.Any(step => step.IsTerminal))
        {
            return (null, "A workflow template must contain at least one terminal step.");
        }

        var requestedByCode = steps.ToDictionary(
            step => NormalizeCode(step.Code),
            StringComparer.OrdinalIgnoreCase);
        var normalizedByCode = normalizedSteps.ToDictionary(
            step => step.Code,
            StringComparer.OrdinalIgnoreCase);
        var userIds = new HashSet<int>();

        foreach (var normalizedStep in normalizedSteps)
        {
            var requestedStep = requestedByCode[normalizedStep.Code];
            var requestedActions = requestedStep.Actions ?? [];

            if (normalizedStep.IsTerminal && requestedActions.Count > 0)
            {
                return (null, $"Terminal step '{normalizedStep.Code}' cannot contain actions.");
            }

            if (!normalizedStep.IsTerminal && requestedActions.Count == 0)
            {
                return (null, $"Non-terminal step '{normalizedStep.Code}' must contain an action.");
            }

            var actionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalizedActions = new List<NormalizedAction>(requestedActions.Count);

            foreach (var action in requestedActions)
            {
                var actionCode = NormalizeCode(action.Code);
                var actionName = action.Name.Trim();
                var toStepCode = NormalizeCode(action.ToStepCode);

                if (!CodePattern().IsMatch(actionCode))
                {
                    return (null, $"Action code '{action.Code}' in step '{normalizedStep.Code}' is invalid.");
                }

                if (actionName.Length is < 2 or > 100)
                {
                    return (null, $"Action '{actionCode}' name must contain between 2 and 100 characters.");
                }

                if (!actionCodes.Add(actionCode))
                {
                    return (null, $"Action code '{actionCode}' is duplicated in step '{normalizedStep.Code}'.");
                }

                if (!normalizedByCode.ContainsKey(toStepCode))
                {
                    return (null, $"Action '{actionCode}' points to unknown step '{toStepCode}'.");
                }

                if (string.Equals(toStepCode, normalizedStep.Code, StringComparison.OrdinalIgnoreCase))
                {
                    return (null, $"Action '{actionCode}' cannot point back to its own step.");
                }

                if (string.Equals(toStepCode, initialSteps[0].Code, StringComparison.OrdinalIgnoreCase))
                {
                    return (null, $"Action '{actionCode}' cannot point to the initial step.");
                }

                var actionerValidation = ValidateActioners(
                    actionCode,
                    action.Actioners,
                    userIds);
                if (actionerValidation.Error is not null)
                {
                    return (null, actionerValidation.Error);
                }

                normalizedActions.Add(new NormalizedAction(
                    actionCode,
                    actionName,
                    toStepCode,
                    action.RequiresComment,
                    actionerValidation.Actioners!));
            }

            normalizedStep.Actions.AddRange(normalizedActions);
        }

        if (validateUserActioners && userIds.Count > 0)
        {
            var activeUserIds = await dbContext.Users
                .AsNoTracking()
                .Where(user => userIds.Contains(user.Id) && user.IsActive)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
            var missingUserIds = userIds.Except(activeUserIds).OrderBy(id => id).ToArray();

            if (missingUserIds.Length > 0)
            {
                return (null, $"Workflow actioner users are missing or inactive: {string.Join(", ", missingUserIds)}.");
            }
        }

        var graphError = ValidateGraph(normalizedSteps, initialSteps[0].Code);
        return graphError is null
            ? (new NormalizedDefinition(normalizedName, normalizedSteps), null)
            : (null, graphError);
    }

    private static (IReadOnlyList<NormalizedActioner>? Actioners, string? Error) ValidateActioners(
        string actionCode,
        IReadOnlyList<WorkflowActionerDefinitionRequest>? requestedActioners,
        ISet<int> userIds)
    {
        var actioners = requestedActioners ?? [];
        if (actioners.Count == 0)
        {
            return (null, $"Action '{actionCode}' must contain at least one actioner.");
        }

        var normalized = new List<NormalizedActioner>(actioners.Count);
        var uniqueActioners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var actioner in actioners)
        {
            if (!Enum.IsDefined(actioner.ActionerType))
            {
                return (null, $"Action '{actionCode}' contains an unknown actioner type.");
            }

            string? key;
            switch (actioner.ActionerType)
            {
                case WorkflowActionerType.Requester:
                    key = null;
                    break;
                case WorkflowActionerType.Role:
                    key = NormalizeCode(actioner.ActionerKey ?? string.Empty);
                    if (!ValidRoles.Contains(key))
                    {
                        return (null, $"Action '{actionCode}' contains unknown role '{key}'.");
                    }
                    break;
                case WorkflowActionerType.User:
                    key = actioner.ActionerKey?.Trim();
                    if (!int.TryParse(
                        key,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var userId) || userId < 1)
                    {
                        return (null, $"Action '{actionCode}' must use a positive user ID for a User actioner.");
                    }
                    userIds.Add(userId);
                    key = userId.ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    return (null, $"Action '{actionCode}' contains an unknown actioner type.");
            }

            var uniqueKey = $"{actioner.ActionerType}:{key}";
            if (!uniqueActioners.Add(uniqueKey))
            {
                return (null, $"Action '{actionCode}' contains duplicate actioners.");
            }

            normalized.Add(new NormalizedActioner(actioner.ActionerType, key));
        }

        return (normalized, null);
    }

    private static string? ValidateGraph(
        IReadOnlyCollection<NormalizedStep> steps,
        string initialStepCode)
    {
        var adjacency = steps.ToDictionary(
            step => step.Code,
            step => step.Actions.Select(action => action.ToStepCode).Distinct().ToArray(),
            StringComparer.OrdinalIgnoreCase);
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(initialStepCode);

        while (queue.TryDequeue(out var code))
        {
            if (!reachable.Add(code))
            {
                continue;
            }

            foreach (var nextCode in adjacency[code])
            {
                queue.Enqueue(nextCode);
            }
        }

        var unreachable = steps
            .Select(step => step.Code)
            .Where(code => !reachable.Contains(code))
            .OrderBy(code => code)
            .ToArray();
        if (unreachable.Length > 0)
        {
            return $"Every step must be reachable from the initial step. Unreachable: {string.Join(", ", unreachable)}.";
        }

        var visitState = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool ContainsCycle(string code)
        {
            if (visitState.TryGetValue(code, out var state))
            {
                return state == 1;
            }

            visitState[code] = 1;
            if (adjacency[code].Any(ContainsCycle))
            {
                return true;
            }

            visitState[code] = 2;
            return false;
        }

        return ContainsCycle(initialStepCode)
            ? "Workflow steps cannot contain a cycle."
            : null;
    }

    private IQueryable<WorkflowProcessTemplate> TemplateQuery(bool tracking)
    {
        var query = dbContext.WorkflowProcessTemplates
            .AsSplitQuery()
            .Include(template => template.Steps)
                .ThenInclude(step => step.Actions)
                    .ThenInclude(action => action.Actioners)
            .Include(template => template.Steps)
                .ThenInclude(step => step.Actions)
                    .ThenInclude(action => action.ToStepTemplate)
            .AsQueryable();

        return tracking ? query : query.AsNoTracking();
    }

    private Task<bool> IsUsedByInstanceAsync(int id, CancellationToken cancellationToken) =>
        dbContext.WorkflowProcessInstances.AnyAsync(
            instance => instance.ProcessTemplateId == id,
            cancellationToken);

    private async Task<WorkflowTemplateResponse> GetRequiredResponseAsync(
        int id,
        CancellationToken cancellationToken) =>
        (await GetByIdAsync(id, cancellationToken))!;

    private static List<WorkflowStepTemplate> BuildSteps(
        IReadOnlyCollection<NormalizedStep> definitions)
    {
        var stepMap = definitions.ToDictionary(
            definition => definition.Code,
            definition => new WorkflowStepTemplate
            {
                Code = definition.Code,
                Name = definition.Name,
                DisplayOrder = definition.DisplayOrder,
                IsInitial = definition.IsInitial,
                IsTerminal = definition.IsTerminal
            },
            StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            var step = stepMap[definition.Code];
            step.Actions = definition.Actions.Select(action => new WorkflowActionTemplate
            {
                Code = action.Code,
                Name = action.Name,
                RequiresComment = action.RequiresComment,
                ToStepTemplate = stepMap[action.ToStepCode],
                Actioners = action.Actioners.Select(actioner => new WorkflowActionerTemplate
                {
                    ActionerType = actioner.ActionerType,
                    ActionerKey = actioner.ActionerKey
                }).ToList()
            }).ToList();
        }

        return stepMap.Values.OrderBy(step => step.DisplayOrder).ToList();
    }

    private static UpdateWorkflowTemplateRequest DefinitionFromTemplate(
        WorkflowProcessTemplate template)
    {
        var stepCodes = template.Steps.ToDictionary(step => step.Id, step => step.Code);

        return new UpdateWorkflowTemplateRequest
        {
            Name = template.Name,
            Steps = template.Steps
                .OrderBy(step => step.DisplayOrder)
                .Select(step => new WorkflowStepDefinitionRequest
                {
                    Code = step.Code,
                    Name = step.Name,
                    DisplayOrder = step.DisplayOrder,
                    IsInitial = step.IsInitial,
                    IsTerminal = step.IsTerminal,
                    Actions = step.Actions
                        .OrderBy(action => action.Id)
                        .Select(action => new WorkflowActionDefinitionRequest
                        {
                            Code = action.Code,
                            Name = action.Name,
                            ToStepCode = stepCodes[action.ToStepTemplateId],
                            RequiresComment = action.RequiresComment,
                            Actioners = action.Actioners
                                .OrderBy(actioner => actioner.Id)
                                .Select(actioner => new WorkflowActionerDefinitionRequest
                                {
                                    ActionerType = actioner.ActionerType,
                                    ActionerKey = actioner.ActionerKey
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private static WorkflowTemplateResponse ToResponse(WorkflowProcessTemplate template)
    {
        var stepCodes = template.Steps.ToDictionary(step => step.Id, step => step.Code);
        return new WorkflowTemplateResponse(
            template.Id,
            template.Code,
            template.Name,
            template.EntityType,
            template.Version,
            template.IsPublished,
            template.IsActive,
            template.CreatedAtUtc,
            template.PublishedAtUtc,
            template.Steps
                .OrderBy(step => step.DisplayOrder)
                .Select(step => new WorkflowTemplateStepResponse(
                    step.Id,
                    step.Code,
                    step.Name,
                    step.DisplayOrder,
                    step.IsInitial,
                    step.IsTerminal,
                    step.Actions
                        .OrderBy(action => action.Id)
                        .Select(action => new WorkflowTemplateActionResponse(
                            action.Id,
                            action.Code,
                            action.Name,
                            stepCodes[action.ToStepTemplateId],
                            action.RequiresComment,
                            action.Actioners
                                .OrderBy(actioner => actioner.Id)
                                .Select(actioner => new WorkflowTemplateActionerResponse(
                                    actioner.Id,
                                    actioner.ActionerType,
                                    actioner.ActionerKey))
                                .ToList()))
                        .ToList()))
                .ToList());
    }

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

    private static string? NormalizeOptionalCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeCode(value);

    private static WorkflowTemplateOperationResult Success(WorkflowTemplateResponse template) =>
        new(WorkflowTemplateOperationStatus.Success, template);

    private static WorkflowTemplateOperationResult NotFound() =>
        new(WorkflowTemplateOperationStatus.NotFound, ErrorMessage: "Workflow template was not found.");

    private static WorkflowTemplateOperationResult ValidationFailed(string message) =>
        new(WorkflowTemplateOperationStatus.ValidationFailed, ErrorMessage: message);

    private static WorkflowTemplateOperationResult Conflict(string message) =>
        new(WorkflowTemplateOperationStatus.Conflict, ErrorMessage: message);

    private static WorkflowTemplateOperationResult InvalidState(string message) =>
        new(WorkflowTemplateOperationStatus.InvalidState, ErrorMessage: message);

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityTypePattern();

    private sealed record NormalizedDefinition(
        string Name,
        IReadOnlyList<NormalizedStep> Steps);

    private sealed record NormalizedStep(
        string Code,
        string Name,
        int DisplayOrder,
        bool IsInitial,
        bool IsTerminal,
        List<NormalizedAction> Actions);

    private sealed record NormalizedAction(
        string Code,
        string Name,
        string ToStepCode,
        bool RequiresComment,
        IReadOnlyList<NormalizedActioner> Actioners);

    private sealed record NormalizedActioner(
        WorkflowActionerType ActionerType,
        string? ActionerKey);
}
