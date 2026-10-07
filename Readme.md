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
- Role-aware Dashboard with live summaries, actionable reminders, and the latest authorized activity.
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
npm ci
npm run dev
```

Open `http://localhost:5173`.

Set `VITE_API_BASE_URL` in `frontend/.env` only when the API uses a different address:

```env
VITE_API_BASE_URL=http://localhost:5165
```

For a production build, `frontend/.env.production` sets `VITE_API_BASE_URL` to an empty
string. Requests then use same-origin paths such as `/api/auth/login`; configure Nginx
to forward `/api/` to the backend while serving the Vue build. Do not set this value
to `/api`, because the service paths already include that prefix. Local development
continues to use `http://localhost:5165`. Rebuild the frontend after changing its
environment configuration, and never put passwords or JWT signing keys in `VITE_*`
variables: these values are exposed to browsers.

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
npm ci
npm run lint:check
npm run test:unit -- --run
npm run build
```

Install the Playwright browser once, then run browser tests:

```powershell
npx playwright install chromium
npm run test:e2e -- --project=chromium
```

### GitHub Actions CI (tests and build only)

The workflow is [`.github/workflows/ci.yml`](.github/workflows/ci.yml), named **Project1 CI**.
It uses GitHub-hosted Ubuntu 24.04 runners, .NET 10, and Node.js 24.15.0. Your Ubuntu
laptop does not need to be online for CI. This workflow does **not** deploy, connect
to your home server, apply database migrations, or send emails. No GitHub deployment
secrets or self-hosted runner are required.

#### When does it run?

| Trigger | What happens |
| --- | --- |
| Open, reopen, or update a Pull Request targeting `main` | Check the proposed changes before merging. Pushing another commit to that PR runs the checks again. |
| Push to `main`, including merging a PR | Check the updated `main` after the push or merge. |
| Actions → Project1 CI → Run workflow | Run the same checks manually for the selected branch. The workflow must first exist on the default branch for this button to appear. |

Pulling code onto your PC does not trigger CI. Creating a PR and later merging it
normally produces two runs: a PR check before merging and a `main` check afterward.
CI does not block merging by itself; requiring successful checks needs a separate
GitHub branch protection rule or ruleset, which is not configured by this task.

#### What does it check?

The backend and frontend jobs run independently:

- **Backend:** restore dependencies, install PDF fonts, build and run xUnit tests,
  then publish a framework-dependent `linux-x64` API. Tests use isolated SQLite
  databases and test doubles; they do not use the Ubuntu SQL Server or SMTP account.
- **Frontend:** install dependencies with `npm ci`, check Oxlint/ESLint without
  modifying files, run Vitest unit tests, then type-check and create a production
  build. The production API base URL is empty for same-origin Nginx `/api/` requests.

This first CI does not run Playwright browser tests or `Test-EndToEnd.ps1`. Some
browser tests still contain older page expectations; the full API smoke script
requires a running API and creates business records. These can be addressed in a
separate test-improvement task.

Frontend dependencies are managed with **npm**, not pnpm. Commit both `package.json`
and `package-lock.json` when changing dependencies. Use `npm ci` for a fresh install
from the lock file; use `npm install <package>` when intentionally adding a dependency.
The previous pnpm lock file and Vue release-candidate overrides have been removed.
Oxlint is aligned to `~1.73.0` because `eslint-plugin-oxlint` currently requires that
version range. `npm run lint:check` does not edit files; `npm run lint` still runs
the existing automatic-fix commands.

#### How to test CI on GitHub

1. Commit and push these changes on your feature branch.
2. Create a Pull Request from that branch into `main`.
3. Open the PR's **Checks** tab, or **Actions → Project1 CI**, and inspect the run.
4. Confirm that **Backend tests and Linux publish** and **Frontend checks and
   production build** both pass. If either fails, open its failed step and read the log.
5. Merge the PR when ready, then confirm that the new `main` run also passes.
6. Once the workflow is on `main`, use **Run workflow** to test the manual trigger.

For a simple repeat-trigger test, push a small documentation change to an open PR;
another PR CI run should appear. A push to a feature branch without a PR targeting
`main` does not trigger this workflow.

#### Where are the build files?

Open a completed workflow run and scroll to **Artifacts**. Build artifacts are saved
only after their own job's checks pass; test reports are saved when produced, even
when tests fail. Each artifact name includes the checked commit SHA and is retained
for 14 days:

| Artifact | Contents |
| --- | --- |
| `project1-api-linux-x64-<sha>` | API publish files, including `Project1.Api.dll`; no `appsettings.Development.json`. |
| `project1-frontend-dist-<sha>` | Vue production files, including `index.html` and `assets/`. |
| `backend-test-results-<sha>` | xUnit results in TRX format. |
| `frontend-test-results-<sha>` | Vitest results in JUnit XML format. |

After downloading and extracting a build ZIP, the publish files are at the archive
root: there is no enclosing `api` or `dist` folder. Extract into appropriately named
folders before following the existing manual deployment procedure.

These are build outputs, not automatic deployment or GitHub Releases. For deployment,
use artifacts from a successful **trusted `main` run**, not an unreviewed PR. Both jobs
must be green: one job's artifact can exist even if the other job failed. The API
still needs the .NET 10 runtime, production environment configuration, SQL Server,
and PDF fonts on Ubuntu. Database schema changes still require a separately reviewed
migration and backup procedure; this workflow does not generate or apply migrations.

Keep database/SMTP passwords and JWT signing keys out of tracked configuration and
build artifacts. `/etc/project1/project1.env` remains on Ubuntu. Do not upload it, PC
User Secrets, or `.env.local` files. The workflow uses a read-only repository token
and Actions pinned to commit SHAs, and does not execute public PRs on the home server.

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
- Role-aware Dashboard and live operational reminders.
- Project documentation split into focused guides.

### Learning and deployment roadmap

Follow these phases in order. The manual LAN deployment has been tested; the current
focus is Phase 3's first step: CI tests and build artifacts, without automatic
deployment. Docker begins in Phase 4, after the manual deployment and its automated
deployment pipeline are working. Public access and HTTPS remain separate work.

| Phase | Learning goal | Status | Completion target |
| --- | --- | --- | --- |
| 1 | Develop Vue + .NET on the development PC | Completed for the current feature set | Run and test the full purchasing and inventory process locally. |
| 2 | Manually deploy to Ubuntu Server | LAN deployment tested; public HTTPS not configured | Run the frontend, API, and database on Ubuntu and access the application through a link. |
| 3 | Automate deployment with CI/CD, without Docker | CI workflow added; GitHub verification and CD pending | Use GitHub Actions to test, build, and later deploy the application to Ubuntu. |
| 4 | Learn Docker and containerize locally | Pending | Run the Vue frontend, .NET API, and SQL Server together on the development PC using Docker Compose. |
| 5 | Manually deploy Docker to Ubuntu Server | Pending | Deploy and verify the containerized application on Ubuntu. |
| 6 | Automate Docker build and deployment with CI/CD | Pending | Test the application, build container images, and deploy them to Ubuntu through GitHub Actions. |
| 7 | Start learning Local LLM / Qwen | Pending | Run a model locally, understand its hardware needs, and evaluate a useful project integration. |

#### Phase 2: Manual Ubuntu deployment

- Prepare the Ubuntu laptop/server and confirm its hardware and network setup.
- Install the required runtime, database, web server, and PDF fonts.
- Build and transfer the Vue frontend and .NET API to the server.
- Configure the database, migrations, environment variables, JWT signing key, and SMTP credentials.
- Configure the API service, reverse proxy, and HTTPS for external access.
- Verify demo login, the complete business process, email/PDF delivery, and service recovery after a restart.
- Document startup, updates, logs, database backups, and restore steps.

Review production SMTP settings and credentials before the Ubuntu deployment.

#### Phase 3: CI/CD without Docker

- First step implemented: GitHub-hosted backend restore, build, tests, and Linux publish.
- First step implemented: locked npm installation, non-mutating lint, frontend unit
  tests, type-checking, production build, and downloadable artifacts.
- Next: verify the PR, `main` push, and manual CI triggers on GitHub.
- Later, with separate approval: design secure access to the private Ubuntu server,
  deployment credentials, artifact transfer, and service updates.
- Later: verify deployment health, backups, and rollback. No Docker is used in this phase.

#### Phase 4: Local Docker learning

- Create API and Vue Dockerfiles.
- Add SQL Server and Docker Compose for the local container environment.
- Configure environment variables, persistent database storage, health checks, and PDF fonts.
- Verify the same business process in the local container environment.

#### Phase 5: Manual Docker deployment

- Prepare Docker on Ubuntu and configure the server environment.
- Manually deploy the container images and Compose configuration.
- Verify HTTPS, persistent data, SMTP/PDF delivery, restart behavior, and backups.
- Document manual updates and rollback.

#### Phase 6: Docker CI/CD

- Build and publish container images through GitHub Actions.
- Automate deployment of versioned images to Ubuntu.
- Verify health checks and rollback to the previous image version.

#### Phase 7: Local LLM / Qwen

- Check available hardware and choose a suitable model size.
- Learn local model startup and inference.
- Evaluate a project use case before planning application integration.

### Future business enhancements

- Connect another business module to the generic Workflow Engine when a real approval requirement is identified.


client id = ThC5Q4Azzz11CNTRL-kUCh513QKk11CNTRL
audience (aud claim) = api.tailscale.com/ThC5Q4Azzz11CNTRL-kUCh513QKk11CNTRL
