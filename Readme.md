# Purchase & Inventory Management System

Project1 is a full-stack purchasing management system that demonstrates how an internal company purchase can move from a staff request to approval, supplier quotation, purchase order, and goods receiving.

The application is designed as an interview and portfolio project. It focuses on realistic business rules, role-based access, workflow versioning, audit history, data validation, and a clean separation between the Vue frontend and ASP.NET Core backend.

## Table of contents

- [System overview](#system-overview)
- [Technology stack](#technology-stack)
- [System architecture](#system-architecture)
- [User roles and permissions](#user-roles-and-permissions)
- [Modules and pages](#modules-and-pages)
- [Complete purchasing process](#complete-purchasing-process)
- [Workflow engine](#workflow-engine)
- [Important business rules](#important-business-rules)
- [Getting started](#getting-started)
- [Demo accounts](#demo-accounts)
- [Testing](#testing)
- [Important implementation details](#important-implementation-details)
- [Project structure](#project-structure)
- [Troubleshooting](#troubleshooting)
- [Pending work](#pending-work)

## System overview

The system manages the following purchasing lifecycle:

```text
Organization and catalog setup
        ↓
Purchase request created by requester
        ↓
Department approval
        ↓
Finance approval
        ↓
Supplier quotations and comparison
        ↓
Selected quotation
        ↓
Purchase order issued
        ↓
One or more supplier deliveries received
        ↓
Purchase order fully received
```

The main goals are:

- Give requesters a simple way to request products.
- Route requests to the correct approvers.
- Preserve an audit history for every workflow action.
- Compare quotations from eligible external suppliers.
- Create purchase orders from selected quotations.
- Support partial and full goods receiving.
- Protect pages and API endpoints using authenticated roles.
- Preserve historical records even when master data or workflow templates change.

## Technology stack

### Backend

- ASP.NET Core Web API on .NET 10
- Entity Framework Core 10
- SQL Server
- ASP.NET Core Identity
- JWT bearer authentication
- Role-based authorization
- xUnit backend tests
- SQLite test database for service and controller tests

### Frontend

- Vue 3 Composition API
- TypeScript
- Vite
- Pinia
- Vue Router
- Vitest and Vue Test Utils
- Playwright end-to-end tests
- ESLint, Oxlint, and Oxfmt

## System architecture

```text
Vue pages and components
        ↓
TypeScript service layer
        ↓ HTTP + Bearer JWT
ASP.NET Core controllers
        ↓
Business service layer
        ↓
EF Core DbContext and entities
        ↓
SQL Server
```

The frontend and backend use separate models for different purposes:

- Frontend `types/*.ts` files describe the JSON data used by Vue and TypeScript.
- Backend `DTOs` describe API request and response contracts.
- Backend `Entities` represent database records managed by EF Core.
- Backend `Services` contain business rules and database operations.
- Backend `Controllers` expose HTTP endpoints and translate service results into HTTP responses.

This separation prevents the UI, API contract, and database structure from becoming tightly coupled.

## User roles and permissions

| Role | Main responsibility | Main access |
| --- | --- | --- |
| `REQUESTER` | Creates and submits purchase requests | My Tasks, Purchase Requests |
| `DEPARTMENT_APPROVER` | Checks whether the request is required by the department | My Tasks, Purchase Requests, department approve/reject actions |
| `FINANCE_APPROVER` | Checks budget and financial approval | My Tasks, Purchase Requests, finance approve/reject actions |
| `ADMIN` | Maintains the system and executes the purchasing process | All pages, all workflow actions, master data, users, quotations, purchase orders, and goods receiving |

### Admin as Super Admin

An Admin account only needs the `ADMIN` role. It does not need to also store the Requester, Department Approver, and Finance Approver roles.

The workflow engine treats `ADMIN` as a Super Admin, so Admin can perform any available workflow action. This makes system demonstrations, support, and recovery easier while keeping normal users limited to their assigned responsibilities.

### Route and API security

The application checks access at two levels:

1. Vue Router prevents a user from opening a page that their role cannot access.
2. ASP.NET Core `[Authorize]` attributes protect the API even if someone manually sends an HTTP request.

The backend is the final security authority. Hiding a frontend button is only a user-interface improvement and is not treated as sufficient security.

## Modules and pages

### Login

The Login page authenticates a user using email and password.

After successful login:

- The backend verifies the password through ASP.NET Core Identity.
- The backend creates a signed JWT access token.
- The token contains the user ID, name, roles, and security stamp.
- The frontend stores the current session in `sessionStorage`.
- The common API client automatically sends `Authorization: Bearer <token>`.

Development mode includes four quick-login Demo Account buttons.

### My Tasks

My Tasks is the workflow inbox.

It shows purchase requests that the current account can act on:

- Requester sees their Draft requests that can be edited and submitted.
- Department Approver sees requests waiting for department review.
- Finance Approver sees requests waiting for finance review.
- Admin can perform all currently available actions.

Draft tasks open the Edit form directly. Approval tasks open the details and action dialog.

### Departments

Departments represent internal company departments, for example IT, HR, or Finance.

They are used to:

- Assign users to an organization unit.
- Automatically assign a requester department to a purchase request.
- Display which department requested the products.

Departments can be deactivated instead of being removed from historical records.

### Users

The Users page is an Admin maintenance page for application access.

Admin can:

- Create a user.
- Edit the full name and department.
- Assign one or more business roles.
- Deactivate or reactivate an account.

In Development, a newly created user receives the configured demo password. The UI does not ask the Admin to type a password for every user.

When roles or account status change, the user's security stamp is updated. Existing JWT sessions then become invalid and the user must log in again.

### Workflow Templates

The Workflow Templates page lets Admin design and version approval processes.

A template contains:

- Template code, name, and entity type.
- One initial step.
- Normal review steps.
- One or more terminal steps.
- Actions connecting one step to another.
- Optional required comments.
- Allowed actioners such as Requester, User, or Role.

Admin can:

- Create a Draft template.
- Edit and validate Draft steps and actions.
- Publish a valid version.
- Create a new version from an existing template.
- Delete a Draft version that has no workflow instances.

The order shown in template details follows the actual transition path from the initial step instead of only using the database display order.

> Currently, `PurchaseRequest` is the business entity integrated with the workflow engine. Creating a template for another entity type stores a valid definition, but it will not run automatically until that business module calls the workflow engine.

### Suppliers

Suppliers are external companies or vendors, not internal staff.

Examples include:

- Computer equipment vendor
- Office supply company
- Furniture supplier

The page stores supplier code, name, contact person, email, phone, address, and active status.

### Supplier Products

Supplier Products is the relationship between suppliers and products.

It answers the question:

> Which active supplier is allowed to quote for which product?

This is a many-to-many relationship:

- One supplier can supply many products.
- One product can be supplied by many suppliers.

The relationship can also identify a preferred supplier. Supplier quotation eligibility is calculated from these active relationships.

### Product Categories

Product Categories organize products into groups such as Electronics, Office Furniture, or Stationery.

Categories are master data and can be deactivated while remaining available for historical records.

### Units of Measure

Units of Measure define how product quantities are counted, for example:

- `UNIT`
- `KG`
- `BOX`

Every product refers to one unit of measure.

### Products

Products are the items that employees may request.

A product includes:

- Product code and name
- Description
- Product category
- Unit of measure
- Default estimated unit price
- Reorder level
- Active status

The default price is used as an internal estimate when creating a purchase request. It is not necessarily the final supplier price.

### Purchase Requests

Purchase Requests record an internal need for one or more products.

The requester enters:

- Required date
- Business justification
- Products and quantities

The system automatically gets the requester name and department from the logged-in account. Product estimated prices are copied into the request as a snapshot.

Available operations include:

- `Create draft`: Save the request for later editing.
- `Create and submit`: Create the record and immediately submit it into department review.
- `Edit`: Change a Draft owned by the requester.
- `Submit`: Move Draft to Department Review.
- `Approve` or `Reject`: Available to the correct approver or Admin.
- `Delete`: Remove an eligible Draft.
- `Details`: View items, current workflow step, and immutable workflow history.

### Supplier Quotations

After a purchase request is approved, Admin can record quotations received from external suppliers.

One purchase request can have many supplier quotations. For example:

```text
PR-0010
├── Supplier A quotation: RM 10,000
├── Supplier B quotation: RM 9,500
└── Supplier C quotation: RM 9,800
```

Only suppliers with active Supplier Product relationships for the requested products are eligible.

Quotation statuses:

```text
Draft → Submitted → Selected
                    └─ Other submitted quotations become Not Selected
```

Admin can compare submitted quotations and choose one winner. The selected supplier price becomes the commercial price used by the purchase order.

### Purchase Orders

A Purchase Order is created from one Selected supplier quotation.

The order copies supplier, product, quantity, and price information from the quotation so later changes to master data do not change the historical order.

Purchase Order statuses:

```text
Draft → Issued → Partially Received → Received
              └→ Cancelled
```

- Draft can be edited or deleted.
- Issue confirms that the order was sent to the supplier.
- Only Issued or Partially Received orders can receive goods.
- A fully received order becomes Received.
- An eligible Issued order may be Cancelled with a reason.

### Goods Receiving

Goods Receiving records physical deliveries from a supplier against an issued purchase order.

One purchase order may have multiple Posted goods receipts because a supplier may deliver in parts.

Example:

```text
Purchase Order quantity: 10
First Posted receipt:      6
Remaining quantity:        4
Second Posted receipt:     4
Final PO status:           Received
```

The form displays:

- Ordered quantity
- Previously Posted quantity
- Remaining quantity
- Quantity being received now

Goods Receipt statuses:

```text
Draft → Posted
```

- Draft can be viewed, edited, posted, or deleted.
- Posted is an audit record and cannot be edited or deleted.
- Posting a partial delivery changes the PO to Partially Received.
- Posting all remaining quantities changes the PO to Received.
- The system prevents over-receiving.
- Only one Draft receipt is allowed for a purchase order at a time.
- Duplicate supplier delivery note numbers are rejected for the same purchase order.

### Access Denied

An authenticated user who opens a route without the required role is redirected to a dedicated `403 Access Denied` page.

## Complete purchasing process

### Phase 1: Admin prepares master data

1. Create the required Departments.
2. Create or maintain Users and assign roles.
3. Create Product Categories.
4. Create Units of Measure.
5. Create Products with estimated prices.
6. Create external Suppliers.
7. Link Suppliers to the Products they can supply.
8. Confirm that the `PURCHASE_REQUEST` workflow template is active and published.

### Phase 2: Requester creates a request

1. Log in as `requester@demo.local`.
2. Open Purchase Requests or My Tasks.
3. Click New Purchase Request.
4. Enter the required date and business justification.
5. Add products and quantities.
6. Choose either:
   - Create draft, or
   - Create and submit.

The logged-in user's name and department are assigned automatically. The initial workflow step is Draft.

### Phase 3: Requester submits the Draft

If the request was saved as a Draft:

1. Open My Tasks.
2. Click Edit.
3. Check the information.
4. Submit the request.

The workflow moves from Draft to Department Review.

### Phase 4: Department approval

1. Log in as `department@demo.local`.
2. Open My Tasks.
3. Open the request waiting for Department Review.
4. Approve or Reject it.

If approved, the request moves to Finance Review. If rejected, it moves to the Rejected terminal step. Rejection requires a comment.

### Phase 5: Finance approval

1. Log in as `finance@demo.local`.
2. Open My Tasks.
3. Open the request waiting for Finance Review.
4. Approve or Reject it.

If approved, the request moves to the Approved terminal step. If rejected, it moves to Rejected. Rejection requires a comment.

### Phase 6: Admin records supplier quotations

1. Log in as `admin@demo.local`.
2. Open Supplier Quotations.
3. Create one quotation for each eligible supplier.
4. Enter each supplier's real unit prices, quotation date, validity, and reference.
5. Save and submit each quotation.
6. Open Compare quotations.
7. Select the winning quotation.

The selected quotation becomes Selected and the competing quotations become Not Selected.

### Phase 7: Admin creates and issues a purchase order

1. Open Purchase Orders.
2. Create a Draft order from the Selected quotation.
3. Confirm order date, delivery date, delivery address, and notes.
4. Save the Draft.
5. Click Issue.

The purchase order can no longer be edited after it is issued.

### Phase 8: Admin receives supplier delivery

1. Open Goods Receiving.
2. Click New Goods Receipt.
3. Select an Issued or Partially Received purchase order.
4. Enter the supplier delivery note, received date, notes, and received quantities.
5. Save the Draft.
6. Check the details.
7. Click Post.

If some quantities are still outstanding, the purchase order becomes Partially Received. Repeat the receiving process for later deliveries. When all quantities are received, the purchase order becomes Received.

## Workflow engine

The workflow implementation uses a simplified **Template + Instance** pattern.

### Template tables

Templates define how future workflows should operate:

- `WorkflowProcessTemplates`
- `WorkflowStepTemplates`
- `WorkflowActionTemplates`
- `WorkflowActionerTemplates`

### Instance tables

Instances preserve how one specific business record operates:

- `WorkflowProcessInstances`
- `WorkflowStepInstances`
- `WorkflowActionInstances`
- `WorkflowActionerInstances`
- `WorkflowHistory`

### How a workflow starts

When a Purchase Request is created:

1. The engine finds the latest active, published template for `PurchaseRequest`.
2. It checks that the template has exactly one initial step.
3. It copies the template steps, actions, and actioners into instance records.
4. A Requester actioner is resolved to the actual creator user ID.
5. The workflow starts at the initial Draft step.
6. A `START` audit history entry is created.

### Why the template is copied

Existing records must not unexpectedly change when Admin publishes a new process.

Example:

```text
Version 1: Draft → Department Review → Finance Review → Approved
Version 2: Draft → Finance Review → Department Review → Approved
```

A request created with Version 1 continues using its Version 1 instance. Only new requests use the newly published Version 2 template.

### Workflow authorization

Before moving to another step, the engine checks:

- Is the action available from the current step?
- Is a required comment provided?
- Is the current user the original requester, an assigned user, or in the required role?
- Is the current user an Admin Super User?
- Has the workflow already completed?

Every successful action creates an immutable Workflow History entry containing the previous step, next step, action code, user name, comment, and timestamp.

## Important business rules

### Snapshot data

Transactional records copy important names, codes, quantities, and prices instead of relying only on current master data.

This protects historical accuracy. For example, changing a product default price later must not change an old purchase request, quotation, or purchase order.

### Deactivation instead of destructive deletion

Master data such as departments, suppliers, products, and users can be deactivated. Historical transactions can still refer to those records while new transactions only use active data.

### Frontend and backend validation

The frontend provides immediate user-friendly validation, but the backend repeats all important validation because API requests cannot be trusted.

Examples include:

- Required fields and maximum lengths
- Valid email format
- Valid dates
- Active related records
- Legal status transitions
- Role and workflow authorization
- Supplier eligibility
- Duplicate quotation or purchase order prevention
- Goods receipt remaining quantity checks

### Transaction states

Editing is intentionally limited by state:

- Draft records can usually be changed.
- Submitted, selected, issued, posted, approved, rejected, or completed records are protected.
- State-changing operations use explicit endpoints such as `/submit`, `/select`, `/issue`, `/cancel`, and `/post`.

## Getting started

### Prerequisites

Install the following software:

- .NET 10 SDK
- SQL Server reachable at `localhost`
- Node.js `22.18+` or `24.12+`
- npm
- Git

The default database configuration uses Windows authentication:

```text
Server=localhost;Database=Project1Db;Trusted_Connection=True;TrustServerCertificate=True
```

If your SQL Server configuration is different, override the connection string before starting the backend.

### 1. Restore backend packages and tools

Run from the repository root:

```powershell
dotnet restore Project1.slnx
dotnet tool restore
```

### 2. Configure the database connection when required

PowerShell example:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=Project1Db;Trusted_Connection=True;TrustServerCertificate=True"
```

The double underscore maps to the .NET configuration key `ConnectionStrings:DefaultConnection`.

### 3. Apply EF Core migrations

PowerShell:

```powershell
dotnet tool run dotnet-ef database update `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api
```

One-line version for any shell:

```bash
dotnet tool run dotnet-ef database update --project backend/Project1.Api --startup-project backend/Project1.Api
```

This creates or updates `Project1Db`, including Identity, workflow, purchasing, quotation, purchase order, and goods receipt tables.

### 4. Start the backend

Open terminal 1 at the repository root:

```powershell
dotnet watch --project backend/Project1.Api
```

The backend starts at:

```text
http://localhost:5165
```

Useful development endpoints:

```text
GET http://localhost:5165/api/health
GET http://localhost:5165/openapi/v1.json
```

### 5. Install frontend dependencies

Open terminal 2:

```powershell
cd frontend
npm install
```

Create a local `.env` only if the backend URL is different. The example setting is:

```env
VITE_API_BASE_URL=http://localhost:5165
```

### 6. Start the frontend

From the `frontend` directory:

```powershell
npm run dev
```

Open:

```text
http://localhost:5173
```

### Starting the project on later days

After the first setup, normally only two commands are required:

Terminal 1:

```powershell
dotnet watch --project backend/Project1.Api
```

Terminal 2:

```powershell
cd frontend
npm run dev
```

Run `database update` again after pulling a branch that contains a new migration.

## Demo accounts

Development mode seeds these accounts:

| Role | Email | Password |
| --- | --- | --- |
| Requester | `requester@demo.local` | `Project1Demo123!` |
| Department Approver | `department@demo.local` | `Project1Demo123!` |
| Finance Approver | `finance@demo.local` | `Project1Demo123!` |
| Admin | `admin@demo.local` | `Project1Demo123!` |

Development settings enable demo users in `appsettings.Development.json`.

Do not use the demo password or commit a real signing key in a production environment.

### Production environment variables

Example Bash variables:

```bash
export Jwt__SigningKey="replace-with-a-long-random-secret"
export DemoUsers__Enabled="false"
export ConnectionStrings__DefaultConnection="replace-with-production-connection-string"
```

If a public portfolio deployment intentionally enables demo users, configure their password through a protected environment variable instead of committing it:

```bash
export DemoUsers__Enabled="true"
export DemoUsers__DefaultPassword="replace-with-demo-password"
```

## Testing

### Backend tests

Stop a running `dotnet watch` process before running Debug tests, or run the tests in Release mode:

```powershell
dotnet test Project1.slnx -c Release
```

Backend tests cover controllers, authentication, workflow behavior, services, validation, and status transitions.

### Frontend unit tests

From `frontend`:

```powershell
npm run test:unit -- --run
```

### Frontend linting

```powershell
npm run lint
```

### Frontend type check and production build

```powershell
npm run build
```

### End-to-end tests

Install the Chromium test browser once:

```powershell
npx playwright install chromium
```

Run the E2E tests:

```powershell
npm run test:e2e -- --project=chromium
```

### Full frontend verification

```powershell
npm run format
npm run lint
npm run test:unit -- --run
npm run build
npm run test:e2e -- --project=chromium
```

## Important implementation details

### Authentication and JWT lifecycle

The login process is:

```text
Email + password
      ↓
ASP.NET Core Identity verifies credentials and active status
      ↓
JWT service creates a signed access token
      ↓
Frontend stores the session in sessionStorage
      ↓
API client sends Bearer token
      ↓
Backend validates signature, issuer, audience, expiry, user status, and security stamp
```

The access token lifetime is 60 minutes by default.

During Development, the backend generates a temporary random signing key when it starts. Restarting the backend therefore invalidates existing browser tokens; log in again after a backend restart.

### Security stamp validation

Every authenticated API request compares:

```text
SecurityStamp inside JWT
        versus
current SecurityStamp in database
```

If Admin changes a user's roles or deactivates the account, the database security stamp changes. The older JWT then fails validation immediately instead of remaining valid until its normal expiry time.

### Current user context

The backend builds `ICurrentUserContext` from authenticated JWT claims. Services use it to get:

- Current user ID
- Full name
- Department ID
- Roles

This prevents the browser from choosing trusted information such as requester identity, department, action performer, or audit user name.

### API error handling

Backend services return operation results such as Success, Not Found, Invalid State, Unauthorized, or Validation Failed.

Controllers translate these results into meaningful HTTP responses:

- `400 Bad Request`: invalid input
- `401 Unauthorized`: missing, invalid, or expired login
- `403 Forbidden`: authenticated but not allowed
- `404 Not Found`: record does not exist
- `409 Conflict`: invalid state transition or duplicate business record

The frontend API client reads ASP.NET Problem Details and displays useful messages in forms or alerts.

### Consistent user feedback

Successful create, update, submit, approve, select, issue, post, activate, and deactivate operations use the shared Toast component. Form errors remain near the related form so users can correct them without losing their input.

### EF Core migrations

EF Core compares the current entity model with `AppDbContextModelSnapshot` when creating a migration.

Create a new migration only after intentionally changing the database model:

```powershell
dotnet tool run dotnet-ef migrations add MigrationName `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api
```

Then inspect the generated migration before applying it.

Useful migration behavior:

```text
database update              → move forward to the latest migration
database update <Migration>  → move to a specific migration
database update 0            → remove all migrations from the database
```

Do not use `database update 0` on an environment containing important data.

## Project structure

```text
Project1/
├── backend/
│   └── Project1.Api/
│       ├── Authentication/    # Roles, JWT options, demo user seeding
│       ├── Controllers/       # HTTP API endpoints and authorization
│       ├── Data/              # EF Core AppDbContext and model configuration
│       ├── DTOs/              # API request and response contracts
│       ├── Entities/          # Database entities and enums
│       ├── Migrations/        # EF Core database migrations
│       ├── Services/          # Business rules and data operations
│       └── Program.cs         # Dependency injection and HTTP pipeline
├── frontend/
│   ├── e2e/                   # Playwright browser tests
│   └── src/
│       ├── assets/            # Shared application styles
│       ├── components/        # Reusable forms, dialogs, details, and UI
│       ├── composables/       # Shared Vue behavior such as Toast
│       ├── router/            # Routes and role guards
│       ├── services/          # API client and feature API services
│       ├── stores/            # Pinia authentication state
│       ├── types/             # Frontend TypeScript API models
│       └── views/             # Page-level feature views
├── tests/
│   └── Project1.Api.Tests/    # Backend unit and integration-style tests
├── Project1.slnx
└── Readme.md
```

### Where to add a new business module

A typical full-stack module contains:

```text
Backend Entity and enum
        ↓
DbContext mapping and migration
        ↓
Request/response DTOs
        ↓
Service interface and implementation
        ↓
Controller endpoints and authorization
        ↓
Backend tests
        ↓
Frontend TypeScript types
        ↓
Frontend API service
        ↓
Vue form, details, and list page
        ↓
Router and sidebar link
        ↓
Unit and E2E tests
```

## Troubleshooting

### `Build failed` when running EF migration

Run the build separately to see the real compiler error:

```powershell
dotnet build Project1.slnx
```

Fix the build error before running `database update` again.

### API executable is locked

If a test or build says `Project1.Api.exe` is being used by another process, stop the running `dotnet watch` terminal with `Ctrl+C`, then rerun the command.

Alternatively run tests with a separate Release output:

```powershell
dotnet test Project1.slnx -c Release
```

### `401 Unauthorized` after restarting backend

Development uses a temporary JWT signing key. Log out and log in again after restarting the backend.

### `403 Access Denied`

The account is logged in but does not have the required role. Use the correct demo account or update the user's role as Admin, then log in again.

### New action button is disabled

Many pages require an earlier business record:

- Quotation requires an Approved Purchase Request and eligible Supplier Product relationship.
- Purchase Order requires a Selected quotation.
- Goods Receipt requires an Issued or Partially Received purchase order with remaining quantities.
- A PO with an existing Draft goods receipt cannot create a second Draft receipt.

### Frontend cannot reach backend

Confirm:

- Backend is running at `http://localhost:5165`.
- Frontend is running at `http://localhost:5173`.
- `VITE_API_BASE_URL` is correct.
- The backend CORS policy includes the frontend origin.

### Database connection fails

Confirm SQL Server is running, the server name is correct, and the current Windows account has database access. Override `ConnectionStrings__DefaultConnection` when your instance is not available as `localhost`.

## Pending work

1. Inventory stock ledger and stock balance
2. Dashboard and notifications
3. Docker configuration
4. GitHub Actions automated build and test
5. Ubuntu server deployment and production environment configuration
