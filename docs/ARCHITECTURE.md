# Architecture and Implementation Guide

This document explains the important technical decisions and where to find their implementation.

[Back to README](../Readme.md)

## System architecture

```text
Vue pages and components
        ↓
TypeScript feature services
        ↓ HTTP + Bearer JWT
ASP.NET Core controllers
        ↓
Business services
        ↓
EF Core DbContext and entities
        ↓
SQL Server
```

The application is a separated frontend and backend:

- Vue owns presentation, interaction, client-side validation, and route navigation.
- ASP.NET Core owns authentication, authorization, business rules, trusted validation, and persistence.
- SQL Server stores identity, master data, purchasing records, workflow snapshots, email snapshots, and inventory history.

## Backend structure

```text
backend/Project1.Api/
├── Authentication/   # Roles, JWT settings, claims, and demo user seeding
├── Controllers/      # HTTP endpoints and authorization attributes
├── Data/             # AppDbContext and EF Core relationship configuration
├── DTOs/             # Request and response contracts
├── Email/            # SMTP, outbox worker, templates, and PDF generation
├── Entities/         # Persisted domain and workflow models
├── Migrations/       # Versioned database schema changes
├── Services/         # Business rules and database operations
└── Program.cs        # Configuration, dependency injection, middleware, and seeders
```

### Controllers

Controllers are the HTTP boundary. They:

- Define routes and HTTP methods.
- Require authentication and roles through `[Authorize]`.
- Accept and validate DTOs.
- Call a business service.
- Translate service results into `200`, `201`, `204`, `400`, `403`, `404`, or `409` responses.

Controllers do not contain the main business rules. This keeps behavior reusable and testable.

### Services

Services contain trusted business logic, including:

- Ownership and role checks.
- State-transition validation.
- Duplicate prevention.
- Product, Supplier, quotation, and quantity validation.
- Workflow Engine calls.
- Transaction boundaries.
- Response mapping.

Service interfaces make dependencies explicit and simplify controller tests.

### DTOs and Entities

DTOs describe the public API contract. Entities describe database records.

They are separate because:

- Clients should not control audit fields, IDs, status, or trusted user information.
- Database relationships should not be serialized directly.
- API contracts can evolve without exposing EF Core tracking structures.
- Read responses can combine domain, workflow, and calculated data.

### AppDbContext

`Data/AppDbContext.cs` configures:

- Identity users and roles.
- Primary and foreign keys.
- Unique indexes.
- Decimal precision.
- Delete behavior that protects historical records.
- Purchasing, workflow, email, goods-receipt, and inventory relationships.

Database constraints support service validation but do not replace it. The service should return a clear business error before a raw constraint exception reaches the API.

## Frontend structure

```text
frontend/src/
├── assets/       # Shared styling
├── components/   # Reusable feature forms, details, and dialogs
├── composables/  # Shared behavior such as toast notifications
├── router/       # Lazy routes and role guards
├── services/     # Shared API client and feature-specific requests
├── stores/       # Pinia state, primarily authentication
├── types/        # TypeScript API request/response shapes
├── utils/        # Workflow permission and ordering helpers
└── views/        # Page-level list and administration screens
```

### Views and components

A View owns page-level state, list loading, filters, and opening forms or details. Components own focused UI such as:

- Create/edit forms.
- Details dialogs.
- Confirmation dialogs.
- Workflow action dialogs.

After a successful mutation, the page refreshes its list, closes the form/dialog where appropriate, and shows a shared toast.

### TypeScript services

Each module has a service file such as:

- `purchaseRequestService.ts`
- `quotationService.ts`
- `purchaseOrderService.ts`
- `goodsReceiptService.ts`
- `inventoryService.ts`

These services define endpoint calls but do not own page state. They use the shared `apiClient.ts`, which adds the Bearer token and normalizes API errors.

### Frontend types

Files in `frontend/src/types` describe JSON contracts used by Vue and TypeScript. They do not validate untrusted values at runtime and they are not database models.

When a backend DTO changes, update the corresponding frontend type and service call together.

## Authentication and JWT lifecycle

```text
Email + password
        ↓
ASP.NET Core Identity verifies password, lockout, and active status
        ↓
JwtTokenService creates a signed access token
        ↓
Frontend stores the session in sessionStorage
        ↓
apiClient sends Authorization: Bearer <token>
        ↓
Backend validates token and current database user session
```

### Why Identity is used

ASP.NET Core Identity provides:

- Salted password hashing.
- Unique email handling.
- Account lockout after repeated failures.
- Users and roles.
- Security stamps for session invalidation.

The application never stores a plain-text user password in its database.

### JWT contents

The token includes claims required by the application, such as:

- User ID
- Full name
- Roles
- Security stamp

The token is signed. A client can read claims, but changing them invalidates the signature.

### Security stamp validation

Every authenticated API request compares:

```text
SecurityStamp in JWT
        versus
current SecurityStamp in database
```

If Admin changes a user's roles or active status, user maintenance updates the database Security Stamp. Existing tokens then fail validation immediately.

Relevant files:

- `backend/Project1.Api/Services/Authentication/AuthService.cs`
- `backend/Project1.Api/Services/Authentication/JwtTokenService.cs`
- `backend/Project1.Api/Program.cs`
- `frontend/src/stores/auth.ts`
- `frontend/src/services/authSession.ts`
- `frontend/src/services/apiClient.ts`

### Development signing key

If no signing key is configured in Development, `Program.cs` creates a temporary random key. Restarting the backend therefore invalidates earlier browser tokens.

Production refuses to start without an explicitly configured signing key.

## Authorization model

Authorization is enforced at multiple layers:

1. Sidebar visibility avoids showing irrelevant pages.
2. Vue Router redirects unauthorized navigation to `403 Access Denied`.
3. Controllers require roles for every protected operation.
4. Services apply record-level rules such as ownership and current state.
5. Workflow Engine checks the current Action's Actioners.

The backend remains the final authority. Frontend checks improve usability but are not security controls.

`ADMIN` is a Super Admin. The account holds only `ADMIN`, while backend and workflow rules allow it to recover or demonstrate all operations.

## Current user context

`ICurrentUserContext` exposes trusted identity values derived from the validated JWT:

- User ID
- Name
- Department
- Roles

Business services use this context rather than accepting requester, department, creator, approver, or audit-user values from the browser.

Example:

```text
Browser submits required date + items + justification
        ↓
Backend gets requester ID and department from CurrentUserContext
        ↓
Purchase Request is stored with trusted ownership
```

## Dashboard and live reminders

`GET /api/dashboard` returns one role-aware response containing summary cards, actionable reminders, and recent activity. `DashboardService` uses `ICurrentUserContext` as the authorization boundary and builds only the sections relevant to the authenticated roles.

The Dashboard is a read model over existing Purchase Request, quotation, Purchase Order, Goods Receipt, inventory, Product, workflow-history, and email-outbox data. It does not persist a separate Notification entity or read/unread state. This avoids duplicated operational state: completing the underlying work automatically removes or changes the reminder the next time the Dashboard is loaded.

Important behavior:

- Requesters only receive summaries and activity for their own Purchase Requests.
- Dedicated approvers receive the queue for their workflow step.
- Procurement, Warehouse, and Catalog roles receive module-specific operational reminders.
- Multiple roles are merged and stable keys remove duplicate cards, reminders, and activity.
- Admin receives the company-wide Super Admin view.
- Recent Activity is sorted by UTC occurrence time and limited to the latest 10 authorized records.
- Routes returned by the API point to modules the same role is authorized to open.

The frontend `DashboardView` renders this response without rebuilding business authorization rules. Backend role and ownership filtering remains the security boundary.

## Business state and Workflow state

Project1 keeps the generic approval state in the Workflow Instance while business entities keep statuses needed by their domain.

For Purchase Orders:

- Workflow state controls Draft, Pending Approval, and Approved decisions.
- Business status continues through Issued, Partially Received, Received, and Cancelled.

The service synchronizes the business status after a successful workflow action. See [Workflow Engine](WORKFLOW_ENGINE.md) for the full design.

## Snapshot data and audit history

Historical documents must not silently change when master data changes.

Project1 stores snapshots at important boundaries:

- Purchase Request Item copies the Product estimate.
- Quotation Item copies the supplier price.
- Purchase Order copies selected Supplier, Product, quantity, and price information.
- Workflow Instance copies the Template definition.
- Email Record stores rendered recipients, subject, body, Template version, and attachment bytes.

For example, editing a Product price tomorrow does not change yesterday's approved Purchase Request or issued Purchase Order.

## Database transactions

Operations that update multiple records use an EF Core transaction.

Examples:

- Business-record creation plus Workflow Instance start.
- Workflow action plus business-status synchronization.
- PO Issue plus Email Record and PDF attachment creation.
- Goods Receipt Post plus PO status, Inventory Balance, and Inventory Transaction.

The transaction provides an all-or-nothing result. A Posted receipt cannot exist without the matching Inventory movement, and an Issued PO cannot exist without its queued email snapshot.

## Purchase Request pricing

The frontend displays a Product's default Unit Price as read-only. The backend does not trust a browser-supplied price; it loads the Product and copies its current default price into the Purchase Request Item.

This makes the value:

- Consistent with Product master data at creation time.
- Protected from browser manipulation.
- Stable as a historical snapshot after creation.

The estimate is not the final supplier price. Quotations capture real commercial offers later.

## Supplier eligibility and quotations

Supplier Products model a many-to-many relationship:

```text
Supplier A ─┬─ Product 1
            └─ Product 2

Supplier B ─── Product 1
```

A Supplier can quote only when active Supplier Product relationships cover the requested Products.

One approved Purchase Request can have multiple submitted Quotations. A user compares the offers and selects one winner. Selection changes competing submitted Quotations to Not Selected.

## Purchase Order email architecture

### Issue transaction

When Procurement issues an Approved PO, the service:

1. Confirms the state is Approved.
2. Confirms the Supplier has a valid email.
3. Resolves the active `PURCHASE_ORDER_ISSUED` Email Template.
4. Replaces approved placeholders with PO values.
5. Generates the PO PDF.
6. Creates an Email Record with Pending status.
7. Saves rendered recipients, subject, body, Template metadata, and PDF bytes.
8. Changes PO status to Issued.
9. Commits everything in one transaction.

If template rendering or PDF generation fails, the transaction does not issue the PO.

### Background delivery

`EmailOutboxWorker` periodically asks `EmailOutboxProcessor` to deliver Pending records.

```text
Pending Email Record
        ↓
Load saved recipients, body, and attachments
        ↓
SmtpEmailSender sends snapshot
        ↓
Sent or Failed + attempt/error information
```

Sending happens outside the HTTP request, so Issue does not need to wait for an SMTP server.

### Retry versus resend

- Retry resets a Failed record to Pending and reuses the same saved snapshot.
- Resend creates a new Email Record copied from the earlier snapshot.

This distinction preserves an understandable audit trail.

### Email Templates

Email Templates are versioned:

```text
Draft → Active → Superseded
```

Only Draft can be edited or deleted. Publishing a new version supersedes the previous Active version. Existing Email Records retain the exact rendered content and Template version used when they were created.

### Attachments

Attachment metadata and immutable bytes are stored in SQL Server with the Email Record. The UI can view or download the saved PDF, and SMTP sends those same bytes.

Relevant files:

- `backend/Project1.Api/Email/EmailOutboxWorker.cs`
- `backend/Project1.Api/Email/EmailOutboxProcessor.cs`
- `backend/Project1.Api/Email/SmtpEmailSender.cs`
- `backend/Project1.Api/Email/EmailTemplateRenderer.cs`
- `backend/Project1.Api/Email/PurchaseOrderPdfGenerator.cs`
- `backend/Project1.Api/Services/EmailRecords/EmailRecordService.cs`
- `backend/Project1.Api/Services/EmailTemplates/EmailTemplateService.cs`

## Goods Receiving and Inventory

### Draft versus Posted

A Draft Goods Receipt can be corrected or deleted and does not affect Inventory.

Posting is the irreversible business event. The service:

1. Recalculates previously Posted quantities.
2. Prevents receiving more than the PO remainder.
3. Changes the Goods Receipt to Posted.
4. Updates PO status to Partially Received or Received.
5. Updates the Product's Inventory Balance.
6. Adds an immutable Inventory Transaction.
7. Commits all changes together.

### Inventory model

`InventoryBalance` stores the current quantity for fast display. `InventoryTransaction` stores the audit ledger.

```text
Current balance = previous balance + posted movement
```

The ledger records:

- Transaction type.
- Source document and reference.
- Quantity before.
- Quantity change.
- Quantity after.
- User and UTC timestamp.

Inventory is not edited directly. Stock changes must come from a recognized business event.

### Stock status

```text
Quantity = 0                  → Out of stock
0 < Quantity <= ReorderLevel → Low stock
Quantity > ReorderLevel      → Healthy
```

The Inventory summary counts Products rather than summing quantities because values measured in UNIT, KG, and BOX cannot be combined meaningfully.

Relevant files:

- `backend/Project1.Api/Services/GoodsReceipts/GoodsReceiptService.cs`
- `backend/Project1.Api/Services/Inventory/InventoryService.cs`
- `frontend/src/views/goods-receipts/GoodsReceiptListView.vue`
- `frontend/src/views/inventory/InventoryListView.vue`

## Validation strategy

Validation exists in both frontend and backend for different reasons.

### Frontend validation

Vue validates early so users receive immediate feedback near a field. Examples include required fields, email format, positive quantities, date order, and text length.

### Backend validation

The API repeats every trusted rule because requests can bypass the browser. It validates:

- Required and normalized values.
- IDs and active referenced records.
- Ownership and roles.
- Current status and valid transitions.
- Uniqueness.
- Quantity and price boundaries.
- Cross-record rules such as Supplier eligibility and remaining receipt quantity.

Frontend TypeScript types help developers but do not replace runtime validation.

## API error handling

Services return an operation result that describes Success, Not Found, Invalid State, Unauthorized, Validation Failed, or Conflict. Controllers translate it into meaningful HTTP responses:

| Status | Meaning |
| --- | --- |
| `400 Bad Request` | Input failed validation. |
| `401 Unauthorized` | Login is missing, invalid, or expired. |
| `403 Forbidden` | User is authenticated but lacks permission. |
| `404 Not Found` | Requested record does not exist. |
| `409 Conflict` | Current state or duplicate data prevents the operation. |

The frontend API client extracts ASP.NET Problem Details so forms can show useful messages.

## Deactivation instead of deletion

Master data such as Departments, Products, Categories, Units, and Suppliers may already be referenced by historical purchasing records. Deactivation makes the value unavailable for new work while preserving old records and foreign-key integrity.

True deletion is reserved for eligible Draft transactional records that have not become audit history.

## EF Core migrations

Create a migration only after intentionally changing the entity model:

```powershell
dotnet tool run dotnet-ef migrations add MigrationName `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api
```

Inspect both the generated migration and `AppDbContextModelSnapshot`, then apply it:

```powershell
dotnet tool run dotnet-ef database update `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api
```

Do not use `database update 0` against an environment containing important data.

## Configuration and secrets

.NET configuration is layered. Later providers override earlier values:

```text
appsettings.json
        ↓
appsettings.Development.json
        ↓
User Secrets in Development
        ↓
Environment variables
```

Double underscores map environment variables to nested keys:

```text
ConnectionStrings__DefaultConnection
Jwt__SigningKey
Email__Smtp__Password
```

Never commit production connection strings, JWT signing keys, or SMTP credentials.

## Adding a new full-stack module

Use this general sequence:

1. Add Entity and status enum.
2. Configure it in `AppDbContext`.
3. Generate and inspect a migration.
4. Add request and response DTOs.
5. Add service interface, implementation, and operation result.
6. Add controller endpoints and role authorization.
7. Add backend service and controller tests.
8. Add frontend types.
9. Add a feature API service.
10. Add form, details, and list components/views.
11. Add Router and sidebar access.
12. Add frontend component/service/view tests.
13. Add the module to the UI guide and smoke test where appropriate.

If the module needs approval, also follow the integration steps in [Workflow Engine](WORKFLOW_ENGINE.md#adding-workflow-support-to-another-module).

## Testing strategy

### Backend tests

`tests/Project1.Api.Tests` includes:

- Authentication and JWT tests.
- Controller authorization and response mapping.
- Service business rules and state transitions.
- Workflow authorization and versioning.
- Email Template, Email Record, and PDF tests.
- Goods Receipt and Inventory transaction tests.
- Dashboard role filtering, aggregation, deduplication, and controller tests.

SQLite is used for database-backed tests so relational behavior is exercised without depending on the developer's SQL Server database.

Run:

```powershell
dotnet test Project1.slnx -c Release
```

### Frontend tests

Vitest and Vue Test Utils cover API services, forms, views, role behavior, shared toast logic, and utilities.

```powershell
cd frontend
npm run test:unit -- --run
```

Lint and production build provide additional static verification:

```powershell
npm run lint
npm run build
```

Playwright performs browser-level checks:

```powershell
npm run test:e2e -- --project=chromium
```

### Complete API smoke test

`scripts/Test-EndToEnd.ps1` calls the running API as each Demo Role and verifies the complete lifecycle. It is useful after a cross-module change because it proves the modules still work together.

## Production considerations

Before deploying to Ubuntu:

- Set a strong `Jwt__SigningKey`.
- Disable Demo Users unless the portfolio environment intentionally needs them.
- Store database and SMTP secrets as protected environment variables.
- Install `fonts-dejavu-core` for cross-platform PO PDF generation.
- Use HTTPS behind a reverse proxy.
- Restrict CORS to the real frontend origin.
- Apply migrations in a controlled deployment step.
- Add database backup and restore procedures.
- Persist application logs and monitor Failed Email Records.
- Do not expose SQL Server or SMTP credentials to the browser image.
