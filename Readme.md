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
| `PROCUREMENT_OFFICER` | Performs daily purchasing work | Suppliers, Supplier Products, Supplier Quotations, Purchase Orders; read-only Goods Receiving and Inventory |
| `WAREHOUSE_OFFICER` | Receives deliveries and monitors stock | Read-only Purchase Orders, Goods Receiving, Inventory |
| `CATALOG_MANAGER` | Maintains purchasing master data | Product Categories, Units of Measure, Products |
| `ADMIN` | Configures, supports, and recovers the system | All pages and all operations |

The dedicated business roles keep daily work separate from system administration:

| Page or operation | Requester | Department | Finance | Procurement | Warehouse | Catalog | Admin |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Purchase Request create and own Draft | Yes | No | No | No | No | No | Yes |
| Department approval | No | Yes | No | No | No | No | Yes |
| Finance approval | No | No | Yes | No | No | No | Yes |
| Suppliers and Supplier Products | No | No | No | Manage | No | No | Manage |
| Supplier Quotations | No | No | No | Manage | No | No | Manage |
| Purchase Orders | No | No | No | Manage | Read | No | Manage |
| Goods Receiving | No | No | No | Read | Manage | No | Manage |
| Inventory | No | No | No | Read | Manage | No | Manage |
| Product Categories, Units, Products | Reference data only | Reference data only | Reference data only | Reference data only | Reference data only | Manage | Manage |
| Departments, Users, Workflow Templates | No | No | No | No | No | No | Manage |

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

Development mode includes seven quick-login Demo Account buttons.

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

The system automatically gets the requester name and department from the logged-in account. Product
estimated prices are read-only in the form and are copied by the backend from the Product master
record into the request as a historical snapshot. Changing a Product price later does not change an
existing Purchase Request.

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

Project1 automatically generates an internal quotation number such as `QT-0001`. The optional
Supplier quotation reference is different: it is copied manually from the quotation document
received from the supplier, for example `SUP-Q-2026-001`. Leave it empty when the supplier did not
provide a reference.

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

### Inventory

Inventory shows the latest quantity available for every product and the immutable history of stock
movements.

The Inventory page is available to Admin and provides:

- Active product, low-stock, and out-of-stock counts.
- Search by product, category, or unit of measure.
- Category, stock status, and inactive-product filters.
- Current quantity on hand and product reorder level.
- A per-product transaction history with type and date filters.
- The source document, quantity before, quantity change, quantity after, user, and timestamp for each
  movement.

Inventory quantities are not edited directly. Posting a Goods Receipt creates a `GoodsReceipt`
inventory transaction and increases the balance inside the same database transaction. This preserves
an auditable ledger and prevents a Posted receipt from existing without its matching stock movement.

Stock status is calculated as follows:

```text
Quantity = 0                  -> Out of stock
0 < Quantity <= ReorderLevel -> Low stock
Quantity > ReorderLevel      -> Healthy
```

The summary uses product counts instead of adding all quantities together because units such as
`UNIT`, `KG`, and `BOX` cannot be combined into one meaningful total.

### Access Denied

An authenticated user who opens a route without the required role is redirected to a dedicated `403 Access Denied` page.

## Complete purchasing process

### Phase 1: Admin prepares system configuration

1. Create the required Departments.
2. Create or maintain Users and assign roles.
3. Confirm that the `PURCHASE_REQUEST` workflow template is active and published.

### Phase 2: Catalog and Procurement prepare master data

1. Catalog Manager creates Product Categories.
2. Catalog Manager creates Units of Measure.
3. Catalog Manager creates Products with estimated prices.
4. Procurement Officer creates external Suppliers.
5. Procurement Officer links Suppliers to the Products they can supply.

### Phase 3: Requester creates a request

1. Log in as `requester@demo.local`.
2. Open Purchase Requests or My Tasks.
3. Click New Purchase Request.
4. Enter the required date and business justification.
5. Add products and quantities.
6. Choose either:
   - Create draft, or
   - Create and submit.

The logged-in user's name and department are assigned automatically. The initial workflow step is Draft.

### Phase 4: Requester submits the Draft

If the request was saved as a Draft:

1. Open My Tasks.
2. Click Edit.
3. Check the information.
4. Submit the request.

The workflow moves from Draft to Department Review.

### Phase 5: Department approval

1. Log in as `department@demo.local`.
2. Open My Tasks.
3. Open the request waiting for Department Review.
4. Approve or Reject it.

If approved, the request moves to Finance Review. If rejected, it moves to the Rejected terminal step. Rejection requires a comment.

### Phase 6: Finance approval

1. Log in as `finance@demo.local`.
2. Open My Tasks.
3. Open the request waiting for Finance Review.
4. Approve or Reject it.

If approved, the request moves to the Approved terminal step. If rejected, it moves to Rejected. Rejection requires a comment.

### Phase 7: Procurement records supplier quotations

1. Log in as `procurement@demo.local`.
2. Open Supplier Quotations.
3. Create one quotation for each eligible supplier.
4. Enter each supplier's real unit prices, quotation date, validity, and reference.
5. Save and submit each quotation.
6. Open Compare quotations.
7. Select the winning quotation.

The selected quotation becomes Selected and the competing quotations become Not Selected.

### Phase 8: Procurement creates and issues a purchase order

1. Open Purchase Orders.
2. Create a Draft order from the Selected quotation.
3. Confirm order date, delivery date, delivery address, and notes.
4. Save the Draft.
5. Click Issue.

The purchase order can no longer be edited after it is issued.

### Phase 9: Warehouse receives supplier delivery

1. Log in as `warehouse@demo.local` and open Goods Receiving.
2. Click New Goods Receipt.
3. Select an Issued or Partially Received purchase order.
4. Enter the supplier delivery note, received date, notes, and received quantities.
5. Save the Draft.
6. Check the details.
7. Click Post.

If some quantities are still outstanding, the purchase order becomes Partially Received. Repeat the receiving process for later deliveries. When all quantities are received, the purchase order becomes Received.

### Phase 10: Warehouse monitors inventory

1. Open Inventory after posting a Goods Receipt.
2. Confirm the received product's quantity on hand increased.
3. Check whether any products are Low stock or Out of stock.
4. Click View history for a product.
5. Confirm the ledger shows the Goods Receipt number, quantity before, received change, quantity after,
   posting user, and timestamp.

Draft Goods Receipts do not affect inventory. Only the Post action creates a stock movement.

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

This creates or updates `Project1Db`, including Identity, workflow, purchasing, quotation, purchase
order, goods receipt, and inventory ledger tables.

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
| Procurement Officer | `procurement@demo.local` | `Project1Demo123!` |
| Warehouse Officer | `warehouse@demo.local` | `Project1Demo123!` |
| Catalog Manager | `catalog@demo.local` | `Project1Demo123!` |
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

### Complete system example

The following example is useful for an interview demonstration because it passes through every main
business module and uses each business role.

Example data:

| Record | Example |
| --- | --- |
| Product | Ergonomic Keyboard, quantity 10, reorder level 3 |
| Suppliers | Alpha Equipment and Beta Equipment |
| Alpha quotation | RM 115 per unit |
| Beta quotation | RM 108 per unit and selected as the winner |
| First delivery | 6 units |
| Second delivery | 4 units |

Expected process:

```text
Requester creates PR for 10 keyboards
        -> Draft
Requester submits
        -> Department Review
Department Approver approves
        -> Finance Review
Finance Approver approves
        -> Approved / Completed
Procurement Officer records and submits two quotations
        -> Alpha Submitted + Beta Submitted
Procurement Officer selects Beta
        -> Beta Selected + Alpha Not Selected
Procurement Officer creates and issues PO for 10
        -> Issued
Warehouse Officer posts first receipt for 6
        -> PO Partially Received + Inventory 6
Warehouse Officer posts second receipt for 4
        -> PO Received + Inventory 10
```

#### Complete UI test walkthrough

Use a short unique Run Tag, for example `UI0927A`, in names and references. This prevents duplicate
master data when the walkthrough is repeated.

##### 1. Prepare the application

1. Apply the latest migrations and start both backend and frontend.
2. Open `http://localhost:5173`.
3. Sign in as Demo Admin.
4. Open Workflow Templates.
5. Confirm `PURCHASE_REQUEST` has one Active and Published version.

Expected result: Admin can see all administration and purchasing pages.

##### 2. Create the catalog and suppliers with dedicated roles

Sign out, use the Catalog Manager account, and create the product master data:

1. Open Product Categories and click New category.
   - Name: `Interview Equipment UI0927A`
   - Description: `Complete UI test category`
2. Open Units of Measure and click New unit.
   - Code: `TST0927A`
   - Name: `Test Unit UI0927A`
3. Open Products and click New product.
   - Name: `Ergonomic Keyboard UI0927A`
   - Category: the category created above
   - Unit: the unit created above
   - Default unit price: `120.00`
   - Reorder level: `3`
4. Sign out and use the Procurement Officer account.
5. Open Suppliers and create two suppliers:
   - `Alpha Equipment UI0927A`
   - `Beta Equipment UI0927A`
6. Open Supplier Products and click New relationship twice:
   - Link Alpha Equipment to Ergonomic Keyboard.
   - Link Beta Equipment to Ergonomic Keyboard and mark it Preferred.

Expected result: both suppliers are eligible to quote for the new product. Record the generated
product and supplier codes if you want to search for them later.

##### 3. Create a Draft Purchase Request as Requester

1. Sign out and use the Demo Requester quick-login account.
2. Open Purchase Requests.
3. Click New purchase request.
4. Enter:
   - Required date: any future date
   - Business justification: `Ten keyboards for new employee onboarding UI0927A`
   - Product: `Ergonomic Keyboard UI0927A`
   - Quantity: `10`
   - Confirm that Unit price automatically shows RM 120.00 and cannot be edited
5. Click Create draft.

Expected result: the request has workflow step `Draft`, its stored unit-price snapshot is RM 120.00,
and its estimated total is RM 1,200.00.

To test Draft editing and submission:

1. Open My Tasks.
2. Click Edit on the Draft request.
3. Check the values and click Save and submit.

Expected result: the form closes, a success toast appears, and the request moves to
`Department Review`.

##### 4. Perform Department Review

1. Sign out and use the Department Approver account.
2. Open My Tasks.
3. Open Details for the request waiting at Department Review.
4. Click Approve department review.
5. Enter comment: `Department approved UI walkthrough.`
6. Click Approve department review in the confirmation dialog.

Expected result: the request disappears from the Department Approver task list and moves to
`Finance Review`.

##### 5. Perform Finance Review

1. Sign out and use the Finance Approver account.
2. Open My Tasks.
3. Open the same request.
4. Click Approve finance review.
5. Enter comment: `Budget confirmed for UI walkthrough.`
6. Confirm the action.

Expected result: the workflow becomes `Completed`, the current step becomes `Approved`, and no more
workflow actions are available.

##### 6. Record and compare supplier quotations as Procurement

1. Sign out and use the Procurement Officer account.
2. Open Supplier Quotations and click New quotation.
3. Create the Alpha quotation:
   - Purchase request: the Approved request created above
   - Supplier: `Alpha Equipment UI0927A`
   - Supplier quotation reference (optional): `ALPHA-UI0927A`
   - Quotation date: today
   - Valid until: a future date
   - Supplier unit price: `115.00`
   - Click Save and submit
4. Click New quotation again and create the Beta quotation:
   - Same purchase request
   - Supplier: `Beta Equipment UI0927A`
   - Supplier quotation reference (optional): `BETA-UI0927A`
   - Supplier unit price: `108.00`
   - Click Save and submit

Expected result:

- Alpha total is RM 1,150.00.
- Beta total is RM 1,080.00.
- Both quotations have status `Submitted`.

Click Compare on either quotation, check both prices, and click Select winner for Beta Equipment.
Confirm the selection.

Expected result: Beta becomes `Selected`, while Alpha becomes `Not selected`.

##### 7. Create and issue the Purchase Order

1. Open Purchase Orders and click New purchase order.
2. Select the winning Beta quotation.
3. Enter:
   - Order date: today
   - Expected delivery date: a future date
   - Delivery address: `Project1 Main Warehouse`
   - Notes: `Complete UI walkthrough order`
4. Confirm the preview shows quantity `10`, unit price RM 108.00, and total RM 1,080.00.
5. Click Create draft.
6. Find the new Draft order and click Issue.
7. Confirm the warning.

Expected result: the Purchase Order status becomes `Issued` and it can no longer be edited or
deleted.

##### 8. Test partial Goods Receiving

Sign out and use the Warehouse Officer account. Before receiving, open Inventory and find
`Ergonomic Keyboard UI0927A`.

Expected result: quantity on hand is `0`, and its status is `Out of stock`.

Create the first receipt:

1. Open Goods Receiving and click New goods receipt.
2. Select the Issued Purchase Order.
3. Enter:
   - Supplier delivery note: `DN-1-UI0927A`
   - Received date: today
   - Notes: `First partial delivery`
   - Quantity received: `6`
4. Click Create draft.
5. Return to Inventory before posting.

Expected result: the inventory quantity is still `0` because a Draft receipt must not update stock.

Return to Goods Receiving, find the Draft receipt, click Post, and confirm.

Expected result:

- Goods Receipt status becomes `Posted`.
- Purchase Order status becomes `Partially Received`.
- Inventory quantity becomes `6`.
- Inventory status becomes `Healthy` because 6 is above the reorder level of 3.

##### 9. Receive the remaining quantity

1. Open Goods Receiving and create another receipt for the same Purchase Order.
2. Enter delivery note `DN-2-UI0927A`.
3. The form should show:
   - Ordered: `10`
   - Previously received: `6`
   - Remaining: `4`
4. Enter Quantity received `4`.
5. Create the Draft and Post it.

Expected result:

- The second Goods Receipt becomes `Posted`.
- The Purchase Order becomes `Received`.
- Final inventory quantity becomes `10`.

##### 10. Verify the Inventory ledger

1. Open Inventory.
2. Search for `Ergonomic Keyboard UI0927A`.
3. Click View history.

Expected ledger:

| Movement | Reference | Before | Change | After |
| --- | --- | ---: | ---: | ---: |
| First Posted receipt | First `GR-xxxx` | 0 | +6 | 6 |
| Second Posted receipt | Second `GR-xxxx` | 6 | +4 | 10 |

Both rows should show transaction type `Goods receipt`, the posting Warehouse Officer, and a
timestamp. Date and transaction-type filters should return the matching rows.

##### 11. Optional permission checks

1. Sign in as Requester and manually open `/inventory`, `/purchase-orders`, or `/users`.
2. Sign in as Procurement Officer and manually open `/users` or `/products`.
3. Sign in as Warehouse Officer and manually open `/quotations`.
4. Sign in as Catalog Manager and manually open `/quotations`.
5. Confirm each unauthorized route displays `403 Access Denied`.
6. Sign in as Admin and confirm every page is available.

This verifies both normal business processing and frontend role navigation. The API separately
enforces the same authorization rules.

### Complete API smoke test

[`scripts/Test-EndToEnd.ps1`](scripts/Test-EndToEnd.ps1) automates the same complete process against a
running local API. It creates unique master data, so it can be run more than once without duplicate
category or unit codes.

Before running it:

1. Apply the latest database migrations.
2. Start the backend at `http://localhost:5165`.
3. Confirm Development demo users are enabled.
4. Confirm an active, published `PURCHASE_REQUEST` workflow template exists.

Run from the repository root:

```powershell
.\scripts\Test-EndToEnd.ps1
```

Use a different API address or demo password when required:

```powershell
.\scripts\Test-EndToEnd.ps1 `
  -BaseUrl "http://localhost:5165" `
  -DemoPassword "Project1Demo123!"
```

The script stops immediately when an expected state is incorrect. A successful run ends with
`Complete system smoke test passed` and prints the created PR, quotation, PO, receipt, product, and
final inventory information.

The script intentionally leaves its records in the development database. Completed workflow,
quotation, purchase order, posted receipt, and inventory ledger records are audit history and should
not be deleted automatically. Use a disposable development database when a clean database is needed
after every run.

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

The account is logged in but does not have the required role. Use the correct demo account or let an
Admin assign the appropriate role, then log in again.

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

The following tasks are arranged in the planned development order. Complete and verify one task
before starting the next one.

### 1. Send Purchase Orders to suppliers by email

Status: Pending

Current behavior: Issue validates the Purchase Order and changes it from Draft to Issued, but does not
currently send an email.

Planned behavior:

```text
Issue Purchase Order
        -> save Issued status and audit information
        -> create an email outbox record
        -> background worker sends the email
        -> record Pending, Sent, or Failed delivery status
```

Implementation scope:

- Create a Purchase Order email template with placeholders for supplier, PO number, dates, delivery
  address, items, and total amount.
- Replace template placeholders using the Purchase Order snapshot data.
- Send to the Supplier email address.
- Use an outbox/queue so a temporary email failure does not undo a successfully Issued PO.
- Store recipient, attempt count, last error, sent time, and delivery status.
- Allow Admin or Procurement to retry a Failed email.
- Use a local email catcher during development instead of sending real email.
- Add email preview, service, queue, retry, and integration tests.

Completion check: issuing a PO queues one email, successful delivery is recorded as Sent, and a failed
delivery can be retried without issuing the PO again.

### 2. Connect additional business modules to the Workflow Engine

Status: Pending future extension

Creating a Workflow Template for a new Entity Type only stores its definition. It does not run until
the corresponding business service calls the Workflow Engine.

For each new workflow-enabled module:

- Choose a real business entity, for example `PurchaseOrder` approval.
- Call `StartAsync(entityType, entityId)` when the business record is created.
- Use `ExecuteActionAsync` for its workflow actions.
- Return the Workflow Instance in the module response.
- Add My Tasks support and action UI for that entity.
- Preserve Template + Instance snapshot behavior so old records do not change when a new template is
  published.
- Add authorization, transition, history, and versioning tests.

Completion check: publishing a template for the new Entity Type affects new records, while existing
records continue using their copied Workflow Instance.

### 3. Simplify and separate project documentation

Status: Pending

- Keep the root README focused on introduction, quick start, quick demo, and major architecture.
- Move the detailed UI walkthrough to `docs/UI_TEST_GUIDE.md`.
- Move Workflow Engine internals to `docs/WORKFLOW_ENGINE.md`.
- Move detailed architecture and coding notes to `docs/ARCHITECTURE.md`.
- Keep commands and links between documents consistent.

Completion check: a new user can start and demonstrate the project without reading the technical
implementation sections first.

### 4. Dashboard and notifications

Status: Pending

- Role-specific dashboard summaries.
- Pending approval and receiving reminders.
- Low-stock notifications.
- Recent purchasing and inventory activity.

### 5. Docker configuration

Status: Pending

- ASP.NET Core API Dockerfile.
- Vue production Dockerfile.
- SQL Server container for local deployment.
- Docker Compose configuration and environment variables.

### 6. GitHub Actions automated build and test

Status: Pending

- Backend restore, build, and tests.
- Frontend install, lint, unit tests, and production build.
- Optional Playwright browser tests.

### 7. Ubuntu server deployment

Status: Pending

- Production configuration and secrets.
- Database and container deployment.
- HTTPS and reverse proxy.
- Backup, logging, and deployment instructions.
