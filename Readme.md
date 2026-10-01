# Purchase & Inventory Management System

Project1 is a full-stack procurement system built as an interview and portfolio project. It demonstrates how an internal purchase moves from an employee request through approval, supplier quotation, purchase order, goods receiving, and inventory.

The project focuses on realistic business rules, role-based access, versioned workflows, immutable audit history, email delivery, and a clean separation between a Vue frontend and an ASP.NET Core backend.

## Main business flow

```text
Catalog and supplier setup
        ↓
Purchase Request
        ↓
Department approval
        ↓
Finance approval
        ↓
Supplier quotations and comparison
        ↓
Purchase Order preparation and approval
        ↓
Purchase Order issue + supplier email + PDF
        ↓
Partial or full Goods Receiving
        ↓
Inventory balance and transaction ledger
```

## Main features

- JWT authentication with ASP.NET Core Identity and immediate session invalidation through security stamps.
- Eight business roles with frontend route guards and backend API authorization.
- Versioned Workflow Templates and immutable Workflow Instances.
- Purchase Request department and finance approval.
- Supplier eligibility through Supplier Product relationships.
- Multi-supplier quotation comparison and winner selection.
- Purchase Order submission, approval, rejection, issue, and cancellation.
- Versioned email templates, database-backed email outbox, SMTP delivery, and retry/resend controls.
- Generated Purchase Order PDF attachments preserved as historical email snapshots.
- Partial goods receiving with over-receipt prevention.
- Inventory balances backed by an immutable transaction ledger.
- Shared toast feedback, validation, API error handling, and automated tests.

## Technology stack

| Area | Technology |
| --- | --- |
| Frontend | Vue 3, TypeScript, Vite, Pinia, Vue Router |
| Backend | ASP.NET Core Web API, .NET 10, Entity Framework Core 10 |
| Database | SQL Server |
| Authentication | ASP.NET Core Identity and JWT Bearer tokens |
| Email | Database outbox, background worker, SMTP |
| PDF | PDFsharp-MigraDoc |
| Backend tests | xUnit with SQLite test databases |
| Frontend tests | Vitest, Vue Test Utils, Playwright |

## Documentation

Choose the guide that matches what you want to do:

- [UI Test Guide](docs/UI_TEST_GUIDE.md) — complete browser walkthrough using every business role.
- [Workflow Engine](docs/WORKFLOW_ENGINE.md) — templates, instances, actions, authorization, and module integration.
- [Architecture](docs/ARCHITECTURE.md) — frontend/backend structure, authentication, email, inventory, validation, and testing.

## User roles

| Role | Main responsibility |
| --- | --- |
| `REQUESTER` | Creates, edits, and submits their own Purchase Requests. |
| `DEPARTMENT_APPROVER` | Approves or rejects requests at Department Review. |
| `FINANCE_APPROVER` | Approves or rejects requests at Finance Review. |
| `PROCUREMENT_OFFICER` | Maintains suppliers, records quotations, and prepares/issues Purchase Orders. |
| `PURCHASE_ORDER_APPROVER` | Approves or rejects submitted Purchase Orders. |
| `WAREHOUSE_OFFICER` | Posts Goods Receipts and monitors Inventory. |
| `CATALOG_MANAGER` | Maintains Product Categories, Units of Measure, and Products. |
| `ADMIN` | Super Admin with access to all pages and workflow actions. |

See the [UI Test Guide](docs/UI_TEST_GUIDE.md#demo-accounts-and-role-responsibilities) for page-level responsibilities and the complete role-by-role process.

## Quick start

### Prerequisites

Install:

- .NET 10 SDK
- SQL Server reachable at `localhost`, or provide another connection string
- Node.js `22.18+` or `24.12+`
- npm
- Git
- Optional: Docker or Mailpit for local email testing

The default development connection string uses Windows authentication:

```text
Server=localhost;Database=Project1Db;Trusted_Connection=True;TrustServerCertificate=True
```

### 1. Restore backend dependencies

From the repository root:

```powershell
dotnet restore Project1.slnx
dotnet tool restore
```

If your SQL Server connection is different, set it for the current terminal:

```powershell
$env:ConnectionStrings__DefaultConnection = "your-connection-string"
```

### 2. Apply database migrations

```powershell
dotnet tool run dotnet-ef database update `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api
```

### 3. Configure email delivery

The default settings expect an SMTP server on `localhost:1025`. For local email capture, start Mailpit:

```powershell
docker run -d `
  --name project1-mailpit `
  -p 1025:1025 `
  -p 8025:8025 `
  axllent/mailpit
```

Open `http://localhost:8025` to view captured messages. On later days, use:

```powershell
docker start project1-mailpit
```

To use Gmail or another SMTP provider, keep credentials in .NET User Secrets or environment variables. Do not commit passwords or App Passwords to `appsettings.json`.

Example User Secrets structure:

```json
{
  "Email:Smtp:Host": "smtp.gmail.com",
  "Email:Smtp:Port": "587",
  "Email:Smtp:UseSsl": "true",
  "Email:Smtp:Username": "your-account@gmail.com",
  "Email:Smtp:Password": "your-app-password",
  "Email:Smtp:FromAddress": "your-account@gmail.com",
  "Email:Smtp:FromName": "Project1 Purchasing"
}
```

### 4. Start the backend

Open terminal 1 at the repository root:

```powershell
dotnet watch --project backend/Project1.Api
```

The API starts at `http://localhost:5165`.

Useful endpoints:

```text
GET http://localhost:5165/api/health
GET http://localhost:5165/openapi/v1.json
```

### 5. Start the frontend

Open terminal 2:

```powershell
cd frontend
npm install
npm run dev
```

Open `http://localhost:5173`.

Set `VITE_API_BASE_URL` in `frontend/.env` only when the API uses a different address:

```env
VITE_API_BASE_URL=http://localhost:5165
```

## Demo accounts

Development startup seeds the following accounts. Their default password is `Project1Demo123!`.

| Role | Email |
| --- | --- |
| Requester | `requester@demo.local` |
| Department Approver | `department@demo.local` |
| Finance Approver | `finance@demo.local` |
| Procurement Officer | `procurement@demo.local` |
| Purchase Order Approver | `po.approver@demo.local` |
| Warehouse Officer | `warehouse@demo.local` |
| Catalog Manager | `catalog@demo.local` |
| Admin | `admin@demo.local` |

Demo users are enabled only by the Development configuration. Never use the demo password in production.

## Quick interview demo

For the complete field-by-field walkthrough, use the [UI Test Guide](docs/UI_TEST_GUIDE.md). The shortest full demonstration is:

1. Catalog Manager creates a Category, Unit of Measure, and Product.
2. Procurement Officer creates two Suppliers and links both to the Product.
3. Requester creates and submits a Purchase Request.
4. Department Approver approves it.
5. Finance Approver approves it.
6. Procurement Officer submits two quotations and selects the better one.
7. Procurement Officer creates and submits a Purchase Order.
8. Purchase Order Approver approves it.
9. Procurement Officer issues it; the system queues an email and generates a PDF attachment.
10. Warehouse Officer posts a partial receipt and then the remaining receipt.
11. Inventory shows the final balance and both ledger movements.

## Automated testing

### Backend

```powershell
dotnet test Project1.slnx -c Release
```

### Frontend

```powershell
cd frontend
npm run lint
npm run test:unit -- --run
npm run build
```

Install the Playwright browser once, then run browser tests:

```powershell
npx playwright install chromium
npm run test:e2e -- --project=chromium
```

### Complete API smoke test

With the API running in Development:

```powershell
.\scripts\Test-EndToEnd.ps1 `
  -BaseUrl "http://localhost:5165" `
  -DemoPassword "Project1Demo123!"
```

The script creates unique test data and verifies the flow from Purchase Request through Inventory. It intentionally leaves the audit records in the development database.

## Project structure

```text
Project1/
├── backend/Project1.Api/
│   ├── Authentication/       # Roles, JWT configuration, demo users
│   ├── Controllers/          # HTTP endpoints and authorization
│   ├── Data/                 # EF Core DbContext and mappings
│   ├── DTOs/                 # API request and response contracts
│   ├── Email/                # Templates, SMTP, outbox worker, PDF generation
│   ├── Entities/             # Database entities and enums
│   ├── Migrations/           # EF Core database migrations
│   └── Services/             # Business rules and workflow integration
├── frontend/
│   ├── e2e/                  # Playwright browser tests
│   └── src/
│       ├── components/       # Forms, dialogs, details, and shared UI
│       ├── composables/       # Shared behavior such as toast messages
│       ├── router/            # Routes and role guards
│       ├── services/          # API client and feature services
│       ├── stores/            # Pinia authentication state
│       ├── types/             # TypeScript API contracts
│       └── views/             # Page-level modules
├── docs/                     # Detailed project documentation
├── scripts/                  # Complete API smoke test
├── tests/Project1.Api.Tests/ # Backend automated tests
├── Project1.slnx
└── Readme.md
```

## Important development notes

- Stop a running Debug API if build output is locked, or run tests with `-c Release`.
- Log in again after a Development backend restart because its temporary JWT signing key changes.
- Run `database update` after pulling a branch that contains a new migration.
- The backend is the final authorization authority; hidden frontend buttons are not security controls.
- Use deactivation for master data referenced by historical records instead of destructive deletion.
- Use UTC timestamps in stored audit records and convert only for display.
- Install `fonts-dejavu-core` on Ubuntu so Purchase Order PDF generation has a supported font.

For implementation details, see [Architecture](docs/ARCHITECTURE.md).

## Task status

### Completed

- Purchase Request frontend and workflow.
- Authentication, Users, Roles, and Super Admin behavior.
- Workflow Template backend and administration UI.
- Supplier Product relationships.
- Supplier Quotations and comparison.
- Purchase Orders, approval workflow, email issue, and PDF attachment.
- Goods Receiving and partial delivery.
- Inventory balance and immutable transaction ledger.
- Email Records, versioned Email Templates, retry/resend, and attachments.
- Role separation for Procurement, PO Approval, Warehouse, and Catalog.
- Project documentation split into focused guides.

### Pending

1. Dashboard and notifications
   - Role-specific summaries.
   - Pending approval and receiving reminders.
   - Low-stock notifications.
   - Recent purchasing and inventory activity.
2. Docker configuration
   - API and Vue Dockerfiles.
   - SQL Server and Docker Compose.
   - Environment-variable configuration.
3. GitHub Actions
   - Backend restore, build, and tests.
   - Frontend install, lint, tests, and build.
   - Optional Playwright checks.
4. Ubuntu deployment
   - Production secrets and SMTP configuration.
   - Containers, HTTPS, reverse proxy, backups, and logging.
5. Future workflow expansion
   - Connect another business module to the generic Workflow Engine when a real approval requirement is identified.
