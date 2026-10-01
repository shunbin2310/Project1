# Workflow Engine

This document explains how Project1 stores, starts, authorizes, and executes versioned workflows.

[Back to README](../Readme.md)

## Purpose

The Workflow Engine separates approval routing from business records.

A Purchase Request stores purchasing data such as requester, products, quantities, and estimated prices. A Workflow Instance stores its approval state, available actions, authorized actioners, and audit history.

This separation allows an Admin to publish a new workflow version for future records without changing records that are already in progress.

## Core concepts

### Workflow Template

A Template is the reusable design configured by Admin. It contains:

- Code, for example `PURCHASE_REQUEST`.
- Name and Entity Type.
- Version number.
- Draft, Active, or Superseded state.
- Published and active flags.
- Steps, Actions, and allowed Actioners.

Only an active, published Template can start a new Workflow Instance.

### Step

A Step represents the current stage of approval.

Each Template must contain exactly one Initial Step. A Terminal Step ends the workflow when an action moves into it.

Examples:

- Draft
- Department Review
- Finance Review
- Pending Approval
- Approved
- Rejected

### Action

An Action is a transition from one Step to another.

An Action contains:

- Code such as `SUBMIT`, `APPROVE`, or `REJECT`.
- User-facing name.
- Destination Step.
- Whether a comment is required.
- One or more allowed Actioners.

### Actioner

An Actioner defines who can execute an Action.

Project1 supports:

| Type | Meaning |
| --- | --- |
| Requester | The user who started the workflow. Their user ID is copied into the instance. |
| User | One specific configured user. |
| Role | Any active user holding the configured application role. |

`ADMIN` is a Super Admin override. An Admin can execute any available workflow action even if Admin is not explicitly listed as an Actioner.

### Workflow Instance

An Instance is the independent workflow copy attached to one business record.

It stores:

- Business Entity Type and Entity ID.
- Source Template code, name, and version.
- Copied Steps, Actions, and Actioners.
- Current Step.
- Active or Completed status.
- Immutable transition history.

## Template and instance data

### Template tables

```text
WorkflowProcessTemplate
└── WorkflowStepTemplate
    └── WorkflowActionTemplate
        └── WorkflowActionerTemplate
```

These tables describe the reusable workflow definition.

### Instance tables

```text
WorkflowProcessInstance
├── WorkflowStepInstance
│   └── WorkflowActionInstance
│       └── WorkflowActionerInstance
└── WorkflowHistory
```

These tables preserve the exact definition used by a specific business record.

## How a workflow starts

The business service creates its record and calls:

```csharp
workflowEngine.StartAsync(entityType, entityId, requester, cancellationToken)
```

The engine then:

1. Confirms that the business record does not already have an Instance.
2. Finds the newest active, published Template for the Entity Type.
3. Validates that exactly one Initial Step exists.
4. Creates the Process Instance.
5. Copies all Steps, Actions, and Actioners into Instance tables.
6. Replaces a Requester Actioner with the starting user's ID.
7. Sets the Initial Step as current.
8. Adds a `START` Workflow History row.

The business record and Workflow Instance are created inside the same database transaction. If workflow creation fails, the business record is not left partially created.

Relevant files:

- `backend/Project1.Api/Services/Workflows/IWorkflowEngine.cs`
- `backend/Project1.Api/Services/Workflows/WorkflowEngine.cs`
- `backend/Project1.Api/Entities/Workflows/`

## Why the Template is copied

Assume Version 1 is:

```text
Draft → Department Review → Finance Review → Approved
```

Later, Admin publishes Version 2:

```text
Draft → Department Review → Director Review → Finance Review → Approved
```

Records already started with Version 1 must continue using Version 1. They must not suddenly gain a Director Review step halfway through processing.

Therefore:

- Templates define future workflows.
- Instances preserve historical workflows.
- Publishing a new version affects only new business records.

## Executing an action

The business service calls:

```csharp
workflowEngine.ExecuteActionAsync(
    entityType,
    entityId,
    actionCode,
    actor,
    comment,
    cancellationToken)
```

The engine performs these checks:

1. A Workflow Instance exists.
2. The Instance is not already Completed.
3. The requested Action exists on the current Step.
4. A comment is supplied when the Action requires one.
5. The actor is allowed by Requester, User, Role, or Admin override.

When valid, it:

1. Changes the current Step to the Action destination.
2. Marks the Instance Completed if the destination is Terminal.
3. Records Action code, previous Step, next Step, actor, comment, and UTC timestamp.
4. Saves the result.

The frontend may hide or disable unavailable buttons, but authorization is always enforced again by the backend.

## Current user and workflow authorization

The JWT contains identity and role claims. `CurrentUserContext` converts those claims into the trusted actor used by services:

- User ID
- Full name
- Department ID
- Roles

The browser cannot choose a trusted actor name, user ID, or role in the action request. The backend derives them from the validated JWT.

Relevant files:

- `backend/Project1.Api/Services/Authentication/ICurrentUserContext.cs`
- `backend/Project1.Api/Services/Authentication/CurrentUserContext.cs`
- `backend/Project1.Api/Services/Workflows/WorkflowActor.cs`
- `frontend/src/utils/workflowAuthorization.ts`

## Purchase Request workflow

The active `PURCHASE_REQUEST` definition follows this path:

```text
Draft
  └── SUBMIT by Requester
          ↓
Department Review
  ├── APPROVE by DEPARTMENT_APPROVER
  │       ↓
  │   Finance Review
  │       ├── APPROVE by FINANCE_APPROVER → Approved [Terminal]
  │       └── REJECT by FINANCE_APPROVER  → Rejected [Terminal]
  └── REJECT by DEPARTMENT_APPROVER       → Rejected [Terminal]
```

The Purchase Request service:

- Starts a Workflow Instance during record creation.
- Optionally executes `SUBMIT` immediately for Create and Submit.
- Allows only the owner or Admin to edit/delete a Draft.
- Uses Workflow actions for Submit, Department approval, Finance approval, and rejection.
- Returns workflow data with Purchase Request responses for the Details page and My Tasks.

Relevant file:

- `backend/Project1.Api/Services/PurchaseRequests/PurchaseRequestService.cs`

## Purchase Order workflow

The seeded `PURCHASE_ORDER` workflow follows this path:

```text
Draft
  └── SUBMIT by the creating Procurement user
          ↓
Pending Approval
  ├── APPROVE by PURCHASE_ORDER_APPROVER → Approved [Terminal]
  └── REJECT by PURCHASE_ORDER_APPROVER  → Draft
```

The matching business status path continues beyond approval:

```text
Draft → Pending Approval → Approved → Issued → Partially Received → Received
  ↑                               └──────────────→ Cancelled
  └──────────── Reject ────────────┘
```

Approved is Terminal for the approval workflow, but not the end of the Purchase Order business lifecycle. Issue, receiving, and cancellation are domain operations enforced by `PurchaseOrderService` rather than approval transitions.

The separation is intentional:

- Workflow answers: who must approve the order?
- Purchase Order business rules answer: can it be issued, cancelled, or received?

New Purchase Orders start a Workflow Instance when created. A legacy Draft PO without an Instance receives one lazily when Submit is executed.

Relevant files:

- `backend/Project1.Api/Services/PurchaseOrders/PurchaseOrderWorkflow.cs`
- `backend/Project1.Api/Services/PurchaseOrders/PurchaseOrderWorkflowSeeder.cs`
- `backend/Project1.Api/Services/PurchaseOrders/PurchaseOrderService.cs`

## Workflow Template administration

The Admin UI supports:

- Creating a new Draft Template.
- Editing Steps and Actions.
- Reordering Steps for editing convenience.
- Adding Requester, User, or Role Actioners.
- Requiring comments for selected Actions.
- Validating and publishing a Draft version.
- Creating a new Draft version from an existing Template.
- Deleting an unused Draft version.

Important rules:

- A valid Template has exactly one Initial Step.
- Every non-terminal Step needs a usable outgoing path.
- Action destinations must reference Steps in the same Template.
- Codes are normalized and must be unique where required.
- Active/Superseded definitions are historical and are not edited directly.
- A Draft referenced by Workflow Instances cannot be deleted.

The Details view orders Steps by following transitions from the Initial Step. This displays the business path instead of blindly trusting database display order.

Relevant files:

- `backend/Project1.Api/Services/WorkflowTemplates/WorkflowTemplateService.cs`
- `backend/Project1.Api/Controllers/WorkflowTemplatesController.cs`
- `frontend/src/views/workflow-templates/WorkflowTemplateListView.vue`
- `frontend/src/components/workflow-templates/WorkflowTemplateForm.vue`
- `frontend/src/components/workflow-templates/WorkflowTemplateDetails.vue`
- `frontend/src/utils/workflowTemplateOrdering.ts`

## Adding workflow support to another module

Creating a Template with a new Entity Type does not automatically change that module. The business service must explicitly integrate with the engine.

Use this implementation sequence:

1. Define a stable Entity Type string and action/step constants.
2. Decide when the business record starts its workflow.
3. Start the Instance inside the same transaction as business-record creation.
4. Add an action endpoint to the module controller.
5. Build `WorkflowActor` from `ICurrentUserContext`.
6. Call `ExecuteActionAsync` from the business service.
7. Map successful workflow Steps to business statuses when required.
8. Return the Workflow Instance with the module response.
9. Add the module to My Tasks.
10. Add action buttons based on available actions and current-user authorization.
11. Test authorization, invalid transitions, comments, history, rollback, and Template version isolation.

Example service pattern:

```text
Begin database transaction
    ↓
Create business record
    ↓
Save to obtain Entity ID
    ↓
Start Workflow Instance for Entity Type + Entity ID
    ↓
If workflow failed: roll back
    ↓
Commit business record and workflow together
```

## Common misunderstandings

### “I created a Template, so why does nothing use it?”

The Template is only configuration. A business module must call `StartAsync` and `ExecuteActionAsync`. Currently Purchase Requests and Purchase Orders do this.

### “Why can Admin approve without the approver role?”

Admin is intentionally treated as a Super Admin in the engine. The Admin account only stores `ADMIN`; it does not need every business role.

### “Why did an old record keep the old steps?”

That is expected. The record uses a copied Workflow Instance created from its original Template version.

### “Why is Approved terminal for PO when receiving happens later?”

The approval process is complete at Approved. Issue and receiving belong to the Purchase Order lifecycle, not the approval decision itself.

### “Why can a Draft Template sometimes not be deleted?”

Deletion is allowed only when no Workflow Instance references that Template. This protects relational integrity and audit history.

## Test coverage

Backend tests cover:

- Starting from the active published Template.
- Rejecting duplicate Instances.
- Action availability.
- Requester, User, Role, and Admin authorization.
- Required comments.
- Terminal completion.
- History creation.
- Template version isolation.
- Purchase Request and Purchase Order service integration.

Relevant test locations:

- `tests/Project1.Api.Tests/Services/WorkflowEngineTests.cs`
- `tests/Project1.Api.Tests/Services/WorkflowTemplateServiceTests.cs`
- `tests/Project1.Api.Tests/Services/PurchaseRequestServiceTests.cs`
- `tests/Project1.Api.Tests/Services/PurchaseOrderServiceTests.cs`
