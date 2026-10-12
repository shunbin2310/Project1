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

【电脑 PowerShell · 仓库根目录】这一步只更新电脑上的开发数据库，不连接 Ubuntu。
已有项目首次启动或拉取新迁移后，先确认本地连接，再更新到当前代码的最新迁移：

```powershell
$LocalDb = "Server=localhost;Database=Project1Db;Trusted_Connection=True;TrustServerCertificate=True"

dotnet tool run dotnet-ef -- migrations list `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api `
  --connection "$LocalDb" `
  -- --environment Development

dotnet tool run dotnet-ef -- database update `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api `
  --connection "$LocalDb" `
  -- --environment Development
```

只有修改了实体／数据库映射、需要生成新迁移时，才运行 `migrations add`；
不是每次启动项目都运行。完整命令及 Ubuntu 发布步骤见[独立操作迁移指南](#独立操作迁移指南)。
不要把旧迁移名当成日常更新目标：更新到早于已应用版本的目标会回退数据库，可能删除列和数据。

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
to your home server, apply production database migrations, or send emails. No GitHub deployment
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
  databases and test doubles. Migration checks also use disposable SQL Server databases
  for fresh/upgrade tests and the exact generated SQL; they never use the home server.
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
| `project1-migrations-sql-<sha>` | Reviewable SQL, migration manifest, build provenance and SHA-256 checksums. |
| `backend-test-results-<sha>` | Business and migration xUnit results in separate TRX files. |
| `frontend-test-results-<sha>` | Vitest results in JUnit XML format. |

After downloading and extracting a build ZIP, the publish files are at the archive
root: there is no enclosing `api` or `dist` folder. Extract into appropriately named
folders before following the existing manual deployment procedure.

These are build outputs, not automatic deployment or GitHub Releases. For deployment,
use artifacts from a successful **trusted `main` run**, not an unreviewed PR. Both jobs
must be green: one job's artifact can exist even if the other job failed. The API
still needs the .NET 10 runtime, production environment configuration, SQL Server,
and PDF fonts on Ubuntu. Database schema changes still require a separately reviewed
migration and backup procedure. CI generates and tests SQL only in disposable databases;
production migration is manual, not part of application CD. See
[CI migration tests](docs/CI_MIGRATION_TESTS.md),
[read-only precheck](docs/MIGRATION_PRECHECK.md), and
[manual migration checklist](docs/MANUAL_MIGRATIONS.md).

可选的“自动准备 + 手动迁移”：先由你手动停止 API，再在
`Project1 manual deployment` 中只勾选 `prepare_migrations`。
它会上传指定成功 CI 的 SQL 包，并在 Ubuntu 保存、校验新的数据库备份；
**不会执行 migration、启动/停止 API 或部署应用**。先完成一次性的服务器工具安装，
具体步骤见[准备迁移与服务器安装说明](docs/MIGRATION_PREPARATION.md)。
准备完成后仍需审核 SQL、保存受保护的服务器外备份、手动迁移及验证。

For the nullable product-note exercise, follow the
[CI and manual migration practice checklist](docs/CICD_PRACTICE.md).
Apply and verify the reviewed database migration before deploying the new API.

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

Follow these phases in order. Phase 3 has been exercised: CI tests/build artifacts,
manually triggered Ubuntu application deployment, and separately executed manual
database migrations (including adding and removing a practice column). Migration
preparation uploads SQL and creates a backup; it never executes uploaded SQL.
Phase 4's basic local Docker stack has been exercised. PDF and email business
verification remain pending. Next, prepare for Phase 5's manual Ubuntu Docker
practice; public access and HTTPS remain separate work, and the existing Ubuntu
deployment stays unchanged.

| Phase | Learning goal | Status | Completion target |
| --- | --- | --- | --- |
| 1 | Develop Vue + .NET on the development PC | Completed for the current feature set | Run and test the full purchasing and inventory process locally. |
| 2 | Manually deploy to Ubuntu Server | LAN deployment tested; public HTTPS not configured | Run the frontend, API, and database on Ubuntu and access the application through a link. |
| 3 | Automate deployment with CI/CD, without Docker | CI and manual-triggered CD exercised; database migrations remain manual | Use GitHub Actions to test, build, and explicitly deploy the application to Ubuntu. |
| 4 | Learn Docker and containerize locally | Basic Vue + API + isolated SQL Server stack exercised; PDF and email verification pending | Run Vue + API + SQL Server locally; separately verify PDF and email. |
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

- Implemented and exercised: GitHub-hosted backend restore, build, tests, and Linux publish.
- Implemented and exercised: locked npm installation, non-mutating lint, frontend unit
  tests, type-checking, production build, and downloadable artifacts.
- Exercised: manually triggered deployment of verified CI packages to private Ubuntu.
- Exercised: prepare-only SQL upload and database backup while the administrator
  keeps the API stopped; uploaded SQL is never executed by this preparation job.
- Exercised: manual add-column/remove-column migrations, off-server backups, and
  temporary backup restore checks. Application deployment and SQL execution remain
  separate actions. No Docker is used in this phase.
- Full application rollback and ongoing backup drills remain separate practice tasks.

#### Phase 4: Local Docker learning

- Docker Desktop installation and the `hello-world` test have passed.
- First stage: API Dockerfile and an isolated SQL Server with Docker Compose.
- Configure local secrets, persistent database storage, health checks, and PDF fonts.
- Manually apply existing migrations to the empty practice database before starting API.
- Exercised with PC-hosted Vue: login, product operations, and database restart persistence.
- Vue now has a Node build stage and an Nginx runtime container with same-origin API proxying.
- Exercised with container-hosted Vue at `localhost:8081`: login, existing product
  display, and Products page refresh. Further business-flow testing remains pending.
- PDF business verification and a local email capture service remain separate tasks.

#### Phase 5: Manual Docker deployment

- Prepare Docker on Ubuntu and configure the server environment.
- Start with read-only checks of Docker, memory, disk, and ports. Plan an isolated
  practice deployment before changing any existing API service or `Project1Db`.
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



## 本地 Docker 练习：Vue + API + 独立 SQL Server

**这里所有命令都在电脑 PowerShell 的仓库根目录执行，不是在 Ubuntu SSH。**
这是一套新的练习环境，不连接 Windows 原有的 `Project1Db`，也不连接 Ubuntu。
现在可以使用 Vue 容器：Node 只负责编译，Nginx 负责运行时提供网页和转发 API 请求。
已经完成 API／数据库练习的用户，直接从步骤 6 开始；不要重新生成密码或重复迁移。

### 当前验证记录（2026-10-12）

- API 和迁移工具镜像构建成功；练习库已手动应用现有 migrations。
- 使用电脑 Vite 前端验证了登录、分类／单位／产品创建和产品修改。
- 停止并重建 API／SQL Server 容器后，测试产品及 `Docker-Shelf-B2` 仍保留。
- Vue 镜像构建和类型检查通过，223 个前端单元测试通过。
- 临时模拟 API 检查通过：Nginx 页面刷新、API 路径／参数／请求体／令牌转发、
  API 错误状态保留、PDF 响应转发。临时测试不连接练习库或正式库。
- 用户已在 `http://localhost:8081/products` 验证登录、原产品显示及页面刷新。
- 尚未验证：真实 PDF 业务生成和邮件流程；模拟 PDF 转发不代表 PDF 业务已通过。
- 这些记录仅适用于本机练习，不代表已部署 Docker 到 Ubuntu 或完成公网 HTTPS。

### 先记住几个名称

| 名称 | 简单解释 | 本项目用途 |
| --- | --- | --- |
| image（镜像） | 运行模板 | Vue 网页和 Nginx；API 编译结果和运行环境；SQL Server 程序 |
| container（容器） | 根据模板启动的实例 | 分别运行前端、API、SQL Server |
| Dockerfile | 制作镜像的步骤 | 还原依赖、编译 API、安装 PDF 字体 |
| Compose | 描述几个容器怎么一起使用 | API、SQL Server、网络和存储配置 |
| volume（卷） | 独立保存数据的存储 | 重建 SQL Server 容器后保留练习数据；不是备份 |

| 从哪里连接 | 地址／名称 |
| --- | --- |
| 电脑浏览器访问 Vue 容器 | `http://localhost:8081` |
| 电脑访问练习 API | `http://localhost:5166` |
| Nginx 容器转发 API 请求 | `http://api:8080`（保留 `/api/...` 路径） |
| 电脑使用 SSMS 连接练习 SQL Server（SQL 登录） | `localhost,14334` |
| API 容器连接数据库 | `sqlserver,1433` |
| 练习数据库 | `Project1DockerPracticeDb` |
| Compose 项目 | `project1-docker-practice` |

容器里的 `localhost` 是容器自己，不是你的电脑。API 使用 Compose 的服务名
`sqlserver` 连接数据库。三个电脑端口都只绑定 `127.0.0.1`，不对局域网开放。
练习 SQL Server 使用 Developer 版本，只用于开发／测试，不用于正式生产。
当前镜像是 Linux x64，适用于已验证的 `linux/amd64` Docker 引擎。

### 1. 打开 Docker Desktop，准备私有配置

先等 Docker Desktop 显示 `Engine running`。在仓库根目录，只复制一次：

```powershell
if (Test-Path -LiteralPath '.env.docker.local') {
    throw '本地配置已经存在，不覆盖。请检查现有文件。'
}
Copy-Item -LiteralPath '.env.docker.example' -Destination '.env.docker.local'
```

在 IDE 打开 `.env.docker.local`，填写三个空值：

- `SQL_PASSWORD`：新的练习 SQL 密码，至少 16 字符，包含大小写、数字和符号。
  本例连接字符串建议只用英文字母、数字、`! @ # _ -`，不要用空格、分号、引号或 `$`。
- `DEMO_PASSWORD`：新的练习账号密码，至少 16 字符，包含大小写、数字和符号。
- `JWT_SIGNING_KEY`：随机签名密钥，至少 64 字符；例如在本地生成两段随机 GUID 拼接。

这些值只放在电脑上。不要复用正式密码，不要提交或截图这个文件。
`.gitignore` 和 `.dockerignore` 已排除它；容器管理员仍可以读取环境变量，所以这是
**本地练习配置，不是生产环境的秘密管理方案**。不要粘贴 `docker inspect` 或没有
`--quiet` 的 Compose 配置输出，它们可能显示密码。

保存后检查配置。`config --quiet` 只检查格式，不启动容器，也不显示密码：

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml config --quiet
```

每次使用下面的命令，都要保留 `--env-file .env.docker.local -f compose.local.yml`。
Compose 可以读取当前 PowerShell 的同名环境变量；不要提前设置 `SQL_PASSWORD`、
`DEMO_PASSWORD` 或 `JWT_SIGNING_KEY` 为其他环境的值。

### 2. 只启动新的 SQL Server

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait sqlserver
```

首次需要下载微软 SQL Server 镜像，并初始化新的 volume，可能需要几分钟。
`up` 是创建／启动，`-d` 是后台运行，`--wait` 是等数据库健康检查通过。
这个命令不会启动 API，也不会运行 migrations。
如果端口 `14334` 被占用，停止这一步排查；不要停止原来的 SQL Server。

### 3. 构建 API 和手动迁移工具镜像

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml --profile tools build api migrate
```

`build` 只制作镜像，不启动 API、不执行迁移。API 镜像使用多阶段构建：
SDK 负责编译，最后的运行镜像只保留运行库、编译结果、字体和健康检查工具。
API 以非 root 用户运行。迁移工具是独立的 SDK 镜像，复用仓库锁定的 EF CLI 版本。
构建上下文只包含 API 源码和 `dotnet-tools.json`，不包含备份、前端、README 或部署工具。
本地 `appsettings.Development.json` 不打包，练习账号和连接由运行时环境变量提供。

### 4. 手动把 migrations 应用到空的练习库

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml --profile tools run --rm migrate
```

**这一步会修改数据库，但只修改 Compose 内的 `Project1DockerPracticeDb`。**
它执行现有的 `dotnet ef database update`，创建数据库和表，不生成新的 migration，
不读取 Windows 登录凭据，也不导入任何旧数据库的数据。
`--profile tools` 开启手动工具，`run --rm` 运行一次后删除工具容器。
必须看到命令成功结束（例如 `Done.`）才继续。失败时不要直接启动 API。
API 启动只初始化角色、演示账号和模板；它不会自动升级表结构。

### 5. 启动 API 并查看状态

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait api
docker compose --env-file .env.docker.local -f compose.local.yml ps
```

API 启动时读取练习数据库，创建缺失的演示账号和模板。`ps` 显示容器状态。
浏览器打开 `http://localhost:5166/openapi/v1.json`，应能看到 JSON。
这个 Development 接口用于 HTTP 健康检查，不代表每个业务操作都已测试。
第一阶段使用仅限本机的 HTTP，不设置容器 HTTPS；这不是公网部署配置。
API 日志中的找不到 HTTPS 端口警告不等于数据库失败；不要为此修改正式环境配置。
如果端口 `5166` 被占用，先排查，不要停止其他项目服务。

### 6. 构建并启动 Vue 容器（已有练习库从这里开始）

**只添加前端，不删除旧容器／volume，不修改表结构，不生成新 migration。**
保留现有 `.env.docker.local` 和数据库数据；不用再复制模板或换密码。
如果电脑上的 `npm run dev` 还在运行，可以在它的终端按 `Ctrl+C` 正常结束。
新前端使用 `8081`，不会占用原来的 `5173`。

在电脑 PowerShell 的仓库根目录依次执行，上一条成功才继续：

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml config --quiet
```

检查 Compose 配置格式，不启动容器，也不打印密码。

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml build frontend
```

`build frontend` 只制作前端镜像，不启动网页，不执行数据库迁移。
首次需要下载 Node 和 Nginx 基础镜像。`npm ci` 按 `package-lock.json` 安装依赖，
`npm run build` 执行类型检查并编译网页，输出到 `dist`。
最后的运行镜像只复制网页和 Nginx 配置，不包含 Node、源码或 node_modules。
Nginx 使用非 root 镜像，容器内监听 `8080`。
2026-10-12 构建验证时，现有锁定依赖的 `npm audit` 报告 6 项漏洞
（4 high、2 critical），均来自开发工具依赖；`--omit=dev` 检查为 0。
这不代表整套应用没有安全问题。依赖升级另行审阅，不执行 `npm audit fix --force`，
也不因这次本机练习就把服务开放到公网。

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait frontend
docker compose --env-file .env.docker.local -f compose.local.yml ps
```

`up` 创建／启动前端容器，`-d` 表示后台运行，`--wait` 等待健康检查。
Compose 会先确认依赖的 API 和 SQL Server 健康；若它们已停止，会一并启动。
**不会运行 migrate 服务。** 新的空库仍需先完成步骤 4；已有库没有新 migration 则不用重跑。
前端健康检查只确认 Nginx 有响应，不代表登录、API 或数据库操作都成功。
如果 `8081` 已被占用，先排查，不要停止其他项目服务。

浏览器打开 `http://localhost:8081`，登录 `admin@demo.local`，手动填写
现有 `.env.docker.local` 中的 `DEMO_PASSWORD`。不要发送或截图密码。
“Use account” 按钮仍使用前端默认演示密码，与随机练习密码不同，不能据此判断登录失败。

为什么不需要修改 API 的 CORS：网页和请求都使用同一个 `localhost:8081` 地址。
浏览器请求 `/api/...`，Nginx 再转发给 Docker 网络里的 `api:8080`。
构建时把 `VITE_API_BASE_URL` 设为空字符串，所以不是直接请求旧的 `localhost:5165`。
`VITE_*` 值会进入浏览器可读的网页，绝不能把 SQL／JWT／练习账号密码作为前端构建参数。
前端构建上下文单独使用 `frontend/`，其 `.dockerignore` 排除 `.env*` 等私有文件；
根目录的 API `.dockerignore` 保持不变。Compose 不给 frontend 注入私有环境变量。

检查以下项目：

1. 登录后进入 Products，确认之前的 `Docker Test Product` 和 `Docker-Shelf-B2` 仍在。
2. 在 Products 页面按刷新，或直接打开 `http://localhost:8081/products`，不应出现 Nginx 404。
3. 按需修改练习产品、保存并刷新，确认结果保留。只操作练习环境的数据。

这不是开发热更新服务器。以后修改 Vue 源码后，重新执行 `build frontend` 和
`up -d --wait frontend`；Compose 会用新镜像重建前端容器，不需要清空数据库或重跑迁移。
如果只是停止后再启动、源码没有修改，不需要重新 build。

#### 可选：仍用电脑上的 Vite 开发前端

另开一个电脑 PowerShell，在仓库根目录执行：

```powershell
Push-Location frontend
try {
    $PreviousApiBaseUrl = $env:VITE_API_BASE_URL
    $env:VITE_API_BASE_URL = 'http://localhost:5166'
    npm run dev -- --host 127.0.0.1 --port 5173 --strictPort
} finally {
    $env:VITE_API_BASE_URL = $PreviousApiBaseUrl
    Pop-Location
}
```

浏览器使用 `http://localhost:5173`，不要使用 `127.0.0.1:5173`，因为现有 API CORS
允许的是 `http://localhost:5173`。登录练习账号 `admin@demo.local`，密码使用刚才的
`DEMO_PASSWORD`。只在这个新环境创建测试分类、计量单位、产品并练习修改。
旧浏览器会话可能属于原环境；请先退出登录再登录练习账号。
如果 5173 已被原来的前端使用，在那一窗口正常退出前端再运行这里的命令，
不要让 Vite 自动换端口。按 `Ctrl+C` 结束练习前端会恢复这个终端之前的 API 地址。

本轮**不测试真实邮件发送／采购订单发出**。SMTP 固定为 API 容器自己的
`127.0.0.1:1025`，那里没有邮件服务；如果触发发信，记录会失败，不会发到正式服务。
后续先增加本地邮件捕获服务，再测试邮件功能。PDF 字体已包含在 API 镜像中，
但 PDF 业务验证仍需要对应的练习数据，不能仅凭构建成功就认为已经验证。

### 7. 停止／重启，验证练习数据仍保留

先创建一个测试产品，再停止这套练习容器：

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml down
```

普通 `down` 删除本项目容器和网络，但保留数据库 volume。再次启动：

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait sqlserver
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait api
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait frontend
```

重新登录网页，确认刚才的测试产品还在。没有新增 migrations 时不需要重复迁移。
之后代码新增 migration，要先停止这套练习 API，再手动运行步骤 4，最后重建并启动 API。
volume 保留数据不等于备份；这轮不修改你已有的 Ubuntu 备份／恢复流程。

**不要使用 `down -v`、`docker volume prune` 或 `docker system prune --volumes`：
它们可能删除数据库存储。也不要随意改项目名／volume 名或更换已初始化库的 SQL 密码。**
SQL Server 初始化后，改 `.env.docker.local` 的 `SQL_PASSWORD` 不会自动改变库内的密码。

### 出错时先查看什么

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml ps
docker compose --env-file .env.docker.local -f compose.local.yml logs --tail 80 api
docker compose --env-file .env.docker.local -f compose.local.yml logs --tail 80 sqlserver
docker compose --env-file .env.docker.local -f compose.local.yml logs --tail 80 frontend
```

只运行与你的问题有关的查看命令。日志也可能包含测试业务数据，分享前先检查。
数据库健康检查通过但 API 启动报找不到表：确认步骤 4 已成功。
前端出现 502：检查 API 状态和前端日志；不要重新迁移或删除 volume 来解决代理问题。
Products 页面刷新 404：确认访问的是 `8081`，且新前端镜像已经构建并启动。
`config --quiet` 成功并不验证密码强度、数据库连接或 Docker 引擎是否正在运行。
当前配置在 Docker Desktop 的 Linux 引擎运行，首次下载需要网络和足够的内存／磁盘。
只有登录、产品操作、PDF 和数据保留都实际通过，才把对应练习标记完成；
镜像版本标签后续会更新，正式部署前需要另行固定、测试版本并设计备份。

参考：[微软 .NET Docker 镜像](https://github.com/dotnet/dotnet-docker)、
[微软 SQL Server 容器](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker)、
[Docker Compose 启动顺序](https://docs.docker.com/compose/how-tos/startup-order/)、
[Vite 静态部署](https://vite.dev/guide/static-deploy.html)、
[Vite 环境变量](https://vite.dev/guide/env-and-mode)、
[Nginx 代理](https://nginx.org/en/docs/http/ngx_http_proxy_module.html)、
[非 root Nginx 镜像](https://github.com/nginx/docker-nginx-unprivileged)。

## 独立操作迁移指南

适用于已安装好准备工具的 homelab：**CI 验证 → 上传 SQL／备份 → 手动迁移 → 部署前后端**。
本次 StorageLocation 已完成，下面是下次有新迁移时使用的模板，不要重复执行本次旧命令。

范围：像本次一样，给 Products 新增允许为空、旧 API 仍兼容的字段。
删列、改名、改类型、非空列、回填数据需要另行设计、审核和演练，不能直接照搬发布顺序。
删除 CicdPracticeNote 请使用下面独立的[删列练习指南](#删除-cicdpracticenote-练习先更新应用再删数据库列)，不要直接执行新增列指南的全部步骤。
改属性名时 EF 可能生成“删旧列 + 加新列”，不一定保留数据；必须检查生成的迁移。

### 先分清操作位置

| 标记 | 在哪里操作 | 怎么认 |
| --- | --- | --- |
| 【电脑 PowerShell】 | Windows 电脑，开发命令在仓库根目录 | Windows 路径；多行续行符为反引号 |
| 【Ubuntu SSH】 | 已登录服务器的终端 | 提示符类似 shunbin@homelab-server:~$；续行符为反斜杠 |
| 【GitHub】 | 浏览器 Actions / Pull Request | 点击按钮，不是在终端执行 |

PowerShell 输入 ssh 并登录成功后，那个窗口接下来是在 Ubuntu 运行。
以 # 开头的是 PowerShell／Bash 注释；不要复制终端提示符、输出或密码。
所有 REPLACE_ 占位符必须先替换。每段成功、检查输出后才执行下一段，不要一次执行整篇。

| 步骤 | 操作位置 | 内容 |
| --- | --- | --- |
| 1–2 | 电脑 / IDE | 改代码、生成迁移、更新和测试本地库 |
| 3 | GitHub / 电脑 | PR、合并、确认 main CI、下载并审阅 SQL |
| 4 | Ubuntu SSH | 确认正式库起点，停止 API 和其他写入 |
| 5 | GitHub | 只选 prepare_migrations，上传 SQL 并备份 |
| 6–7 | Ubuntu SSH → 电脑 | 创建备份下载副本，下载到电脑并校验 |
| 8 | Ubuntu SSH | 临时恢复和检查，不覆盖正式库 |
| 9–10 | Ubuntu SSH | 执行正式迁移，再验证结构和数据 |
| 11–13 | Ubuntu SSH → GitHub → 网页 | 启动兼容 API、部署同一版本、测试网页 |

没有待执行迁移时，不停 API、不运行准备／恢复／旧 SQL，只做 CI 和应用部署。

### 1. 【电脑 PowerShell / IDE】修改代码并生成迁移

在新功能分支修改实体、AppDbContext 映射、DTO、后端服务、前端表单／列表和测试。
已有分支不用再创建；先查看自己的改动，别覆盖其他工作。

~~~powershell
git status
git switch -c practice/your-new-feature
dotnet tool restore

# 将 AddYourNewColumn 换成描述本次变化的名称
dotnet tool run dotnet-ef -- migrations add AddYourNewColumn `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api `
  -- --environment Development
~~~

migrations add 只生成迁移 .cs、Designer.cs 并更新 ModelSnapshot，**不修改数据库**。
审阅 Up（升级）和 Down（回退），确认与本次变化一致。
看到意外的 DropColumn、删表或类型变化就先停止。不要修改已应用到服务器的历史迁移。

### 2. 【电脑 PowerShell】查看、更新和测试本地库

电脑与 Ubuntu 都叫 Project1Db，但它们是两个不同的数据库。
以下连接是电脑的 localhost，不是 192.168.1.158。

~~~powershell
$LocalDb = "Server=localhost;Database=Project1Db;Trusted_Connection=True;TrustServerCertificate=True"

dotnet tool run dotnet-ef -- migrations list `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api `
  --connection "$LocalDb" `
  -- --environment Development

# 不指定旧目标：更新到当前代码的最新迁移
dotnet tool run dotnet-ef -- database update `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api `
  --connection "$LocalDb" `
  -- --environment Development

dotnet test Project1.slnx -c Release
~~~

Pending 表示该库尚未应用。更新到最新时，会按顺序执行所有缺少的迁移，不只最后一份。
本地 SQL Server 专用测试可能因缺少 CI 配置而跳过；还必须确认 GitHub 上真实 SQL Server 测试通过。
不要把旧的 database update 20261009164508_AddProductExampleAttr 当成日常更新命令：
对已经较新的数据库，这会回退，可能删除后续列及数据。回退不属于正常发布步骤。

前端检查也在电脑运行，结束后会回到仓库根目录：

~~~powershell
Push-Location frontend
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci 失败。' }
    npm run lint:check
    if ($LASTEXITCODE -ne 0) { throw 'lint 失败。' }
    npm run test:unit -- --run
    if ($LASTEXITCODE -ne 0) { throw '前端测试失败。' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw '前端构建失败。' }
} finally {
    Pop-Location
}
~~~

在本地网页测试新字段的保存、编辑、刷新、清空，然后再推送。

### 3. 【GitHub / 电脑】推送、合并并审阅 SQL 包

电脑上检查 git status / git diff，用 IDE 或熟悉的 Git 命令提交并推送本次文件。
要包括实体／映射、DTO／服务、前端、测试、新迁移、Designer、ModelSnapshot。
不要提交密码、.bak 或生产配置。

GitHub 上：

1. 创建 PR 到 main，等待前后端两个 CI 检查成功。
2. 合并后，再等待 main / push CI 成功。
3. 记录该成功 CI 的完整 40 位 commit SHA、run ID、attempt。
   run ID 是 URL 中 actions/runs/ 后的数字，不是 #69 之类的界面序号。
4. 下载并解压对应的 project1-migrations-sql-<SHA>，审阅 migrations.sql。
   完整脚本包含旧迁移，要核对正式库真正待执行的部分和实际结构。
   幂等脚本依赖迁移历史，不会自动修复历史和实际结构的差异。

电脑 PowerShell 检查包内来源和文件哈希：

~~~powershell
& {
    $ErrorActionPreference = 'Stop'
    $Package = 'REPLACE_EXTRACTED_SQL_PACKAGE_DIRECTORY'
    if ($Package -like '*REPLACE_*') { throw '先填写本次解压目录。' }

    Get-Content -LiteralPath (Join-Path $Package 'build-info.txt')
    Get-Content -LiteralPath (Join-Path $Package 'migration-manifest.json')

    $Lines = @(Get-Content -LiteralPath (Join-Path $Package 'SHA256SUMS'))
    if ($Lines.Count -ne 3) { throw '校验清单应有三行。' }
    $Seen = @{}
    foreach ($Line in $Lines) {
        if ($Line -notmatch '^([0-9a-f]{64})  (migrations\.sql|build-info\.txt|migration-manifest\.json)$') {
            throw 'SHA256SUMS 格式异常。'
        }
        $Expected = $Matches[1]
        $Name = $Matches[2]
        if ($Seen.ContainsKey($Name)) { throw '校验清单文件名重复。' }
        $Seen[$Name] = $true
        $Actual = (Get-FileHash -LiteralPath (Join-Path $Package $Name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($Actual -ne $Expected) { throw "哈希不一致：$Name" }
        Write-Output "$Name : OK"
    }
}
~~~

build-info 的仓库、commit、run ID、attempt 必须与选中的成功 main / push CI 相同。
哈希一致只说明内容一致，不证明 SQL 安全；准备 workflow 会另行验证所选 CI 来源。
复杂变化还要对目标结构／受保护恢复副本演练新迁移，不能只依赖通用 CI 测试。

### 4. 【Ubuntu SSH】确认正式库起点，再停止 API

先在电脑 PowerShell 登录，保留 SSH 窗口；下载备份时另开电脑 PowerShell：

~~~powershell
ssh shunbin@192.168.1.158
~~~

以下开始在 Ubuntu SSH 运行。只读查询，记下起点的迁移数量、最新迁移、产品数量和列结构：

~~~bash
env -i PATH=/usr/bin:/bin HOME="$HOME" LANG=C.UTF-8 \
  /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -d Project1Db \
  -C -b -I -x -l 15 -t 30 -W -w 2000 -s "|" \
  -Q "
SET NOCOUNT ON;
SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) AS server_name,
       DB_NAME() AS database_name,
       CONVERT(nvarchar(60), DATABASEPROPERTYEX(DB_NAME(), 'Status')) AS database_status,
       (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS applied_migrations,
       (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory) AS latest_migration,
       (SELECT COUNT_BIG(*) FROM dbo.Products) AS product_count;
SELECT name, TYPE_NAME(user_type_id) AS data_type, max_length, is_nullable
FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Products');
"
~~~

需要 homelab-server / Project1Db / ONLINE，且实际结构符合迁移起点。
没有待执行迁移就不继续停服务／准备；未知历史、缺项、结构不符合时停止。

确认维护时间、唯一操作人，停止其他程序／计划任务写入，再停止 API：

~~~bash
sudo systemctl stop project1-api.service
systemctl show project1-api.service \
  --property=ActiveState --property=SubState --property=MainPID
~~~

必须 inactive / dead / MainPID=0，不停止 mssql-server。
只停止 API 不会阻止其他程序或管理员写入。备份后若恢复写入，正式迁移前必须重新停写并刷新备份。
如果停服务前有人更新过数据，停写后再用上面的只读查询记录起点，避免拿旧数量比较恢复结果。

### 5. 【GitHub】只准备 SQL 和备份

Actions → Project1 manual deployment → Run workflow：

| 输入 | 填写／勾选 |
| --- | --- |
| Use workflow from | main |
| ci_run_id | 第 3 步成功 main / push CI 的 run ID |
| commit_sha | 同一 CI 的完整小写 SHA |
| deploy | 不勾选 |
| check_migrations | 不勾选 |
| prepare_migrations | 只勾选这一项 |

等 Success，打开 Migration preparation ONLY Summary。
它上传 SQL、创建 COPY_ONLY／checksum 备份并 VERIFYONLY；**不执行迁移，不启动／停止 API，不部署应用**。
失败或超时先保留 SSH、检查状态和日志；不要重跑、重新安装或删除记录绕过检查。

### 6. 【Ubuntu SSH】填写本次值，创建备份下载副本

Summary 来自刚刚的准备 workflow，不是单纯合并后的 CI 页面。

example
Commit: ecd96ab74831e7ad1e577b2efa58ef325296b823
CI run: 38123390962, attempt: 1
Prepared at: 2026-10-11T08:21:11.923284+00:00
SQL: /var/lib/project1-migration-prepare/175629af310349048db5f02e9f571b9d/migrations.sql
SQL SHA-256: cad95fedccd5e44b3a870a6c3992ec02a6889e5b288a0e7fb903604b3148070c
Backup: /var/opt/mssql/backup/project1-prepare-175629af310349048db5f02e9f571b9d/Project1Db.bak
Backup SHA-256: f14ff2dfac471f60581f57268ebcc51eb26501b068fd838233886b307e859184
Backup bytes: 13523456

Pending migrations:

20261011073715_AddProductStorageLocation
Backup checksum verification passed; this is NOT an actual restore rehearsal.
API remains stopped. No uploaded SQL was executed. No application was deployed.
Administrator: review SQL, retain a protected off-server backup, manually apply and verify,
then start the compatible API and separately deploy the matching application version.
If writes resume, refresh the backup before migration. Preparation is not execution approval.

| 变量 | 从哪里填写 |
| --- | --- |
| PrepareToken | Summary 的 SQL 路径中，project1-migration-prepare/ 后的 32 位目录名 |
| SqlSha256 | SQL SHA-256 |
| BackupSha256 | Backup SHA-256 |
| BackupBytes | Backup bytes |
| ExpectedLatest | Pending migrations 中最后一项 |
| NewColumn | 本次新增的 Products 字段名，不是 Summary 值 |

先替换以下值，再粘到 Ubuntu；这里只设置终端变量和一个检查函数，不执行迁移：

~~~bash
PrepareToken='REPLACE_32_CHARACTER_TOKEN'
SqlSha256='REPLACE_SQL_SHA256'
BackupSha256='REPLACE_BACKUP_SHA256'
BackupBytes='REPLACE_BACKUP_BYTES'
ExpectedLatest='REPLACE_LAST_PENDING_MIGRATION'
NewColumn='REPLACE_NEW_PRODUCTS_COLUMN'

SqlFile="/var/lib/project1-migration-prepare/$PrepareToken/migrations.sql"
BackupFile="/var/opt/mssql/backup/project1-prepare-$PrepareToken/Project1Db.bak"

# 后面的操作复用此函数，占位符没替换或格式不符就停止
check_inputs() {
    if ! [[ "$PrepareToken" =~ ^[0-9a-f]{32}$ &&
            "$SqlSha256" =~ ^[0-9a-f]{64}$ &&
            "$BackupSha256" =~ ^[0-9a-f]{64}$ &&
            "$BackupBytes" =~ ^[1-9][0-9]*$ &&
            "$ExpectedLatest" =~ ^[0-9]{14}_[A-Za-z][A-Za-z0-9_]*$ &&
            "$NewColumn" =~ ^[A-Za-z][A-Za-z0-9_]*$ &&
            "$ExpectedLatest$NewColumn" != *REPLACE_* &&
            "$SqlFile" = "/var/lib/project1-migration-prepare/$PrepareToken/migrations.sql" &&
            "$BackupFile" = "/var/opt/mssql/backup/project1-prepare-$PrepareToken/Project1Db.bak" ]]; then
        printf 'STOP：先正确填写第 6 步的本次值。\n'
        return 1
    fi
}
~~~

对照 Summary 确认构造出的 SqlFile、BackupFile 完整路径相同。
变量和函数只在当前 SSH 窗口有效；重新登录后需重设，不能沿用上次值。
括号中的操作结束后不保留内部新变量，要记录它打印的下载路径／临时库名。

接下来只创建备份副本，原备份不动：

~~~bash
(
set -eu
check_inputs
[ "$(id -un)" = "shunbin" ] || { printf 'STOP：用户不正确。\n'; exit 1; }
sudo -v

DownloadDir=$(mktemp -d "$HOME/project1-backup-download.XXXXXXXX")
chmod 0700 "$DownloadDir"
Copy="$DownloadDir/Project1Db.bak"
sudo install -m 0600 -o "$(id -u)" -g "$(id -g)" -- "$BackupFile" "$Copy"

Actual=$(sha256sum -- "$Copy" | cut -d ' ' -f 1)
if [ "$Actual" != "$BackupSha256" ] ||
   [ "$(stat -c %s "$Copy")" != "$BackupBytes" ]; then
    printf 'STOP：备份副本哈希或大小不一致。\n'
    exit 1
fi
printf '下载文件：%s\nSHA256：%s\n' "$Copy" "$Actual"
)
~~~

mktemp 创建不重名的目录；0700 只允许目录主人访问；
install 是复制并设置权限，不是安装软件，0600 只允许文件主人读写。
sha256sum 检查内容，stat -c %s 查看字节数。记录打印的完整下载文件路径。

### 7. 【电脑 PowerShell】下载备份并校验

在另开的电脑 PowerShell 运行，不是在 Ubuntu 提示符下执行。
RemoteFile 用第 6 步打印的下载路径；Expected、ExpectedBytes 用 Summary 值。
下载的是整个数据库的 .bak，不是 SQL 脚本，也不只是 Products 表。

~~~powershell
& {
    $ErrorActionPreference = 'Stop'
    $RemoteFile = 'REPLACE_UBUNTU_DOWNLOAD_FILE_PATH'
    $Expected = 'REPLACE_BACKUP_SHA256'
    $ExpectedBytes = 'REPLACE_BACKUP_BYTES'
    if ($RemoteFile -notmatch '^/home/shunbin/project1-backup-download\.[A-Za-z0-9]{8}/Project1Db\.bak$' -or
        $Expected -cnotmatch '^[0-9a-f]{64}$' -or
        $ExpectedBytes -notmatch '^[1-9][0-9]*$') {
        throw '先正确填写本次下载路径、哈希和大小。'
    }

    $Root = Join-Path $env:USERPROFILE 'Project1Backups'
    if (Test-Path -LiteralPath $Root) {
        $Item = Get-Item -LiteralPath $Root
        if (!$Item.PSIsContainer -or
            ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw '备份根目录不是普通文件夹。'
        }
    } else {
        New-Item -ItemType Directory -Path $Root | Out-Null
    }
    $Folder = Join-Path $Root ('Migration-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $Folder | Out-Null

    # 仅当前用户、SYSTEM 和本机管理员可访问，不继承其他用户权限
    $Acl = Get-Acl -LiteralPath $Folder
    $Acl.SetAccessRuleProtection($true, $false)
    $UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach ($SidText in @($UserSid, 'S-1-5-18', 'S-1-5-32-544')) {
        $Sid = [Security.Principal.SecurityIdentifier]::new($SidText)
        $Rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $Sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'
        )
        $Acl.SetAccessRule($Rule)
    }
    Set-Acl -LiteralPath $Folder -AclObject $Acl

    $File = Join-Path $Folder 'Project1Db-before-migration.bak'
    $Remote = 'shunbin@192.168.1.158:{0}' -f $RemoteFile
    scp -o StrictHostKeyChecking=yes $Remote $File
    if ($LASTEXITCODE -ne 0) { throw '下载失败，停止。' }

    $Hash = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Hash -ne $Expected -or
        (Get-Item -LiteralPath $File).Length -ne [long]$ExpectedBytes) {
        throw '下载文件哈希或大小不一致。'
    }
    Write-Output "电脑备份：$File"
    Write-Output "SHA256：$Hash"
    Write-Output '下载校验成功；尚未执行迁移。'
}
~~~

scp 用 SSH 复制文件；StrictHostKeyChecking=yes 只接受电脑已信任的服务器。
主机密钥不被信任或发生变化时，先确认服务器指纹，不要关闭检查绕过。
成功后保留电脑备份位置，不覆盖旧备份、不把 .bak 提交 Git 或公开上传。

### 8. 【Ubuntu SSH】恢复到临时库并检查

先查看备份内的文件名称：

~~~bash
(
set -eu
check_inputs
env -i PATH=/usr/bin:/bin HOME="$HOME" LANG=C.UTF-8 \
  /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -d master \
  -C -b -I -x -l 15 -t 60 -W -w 4000 -s "|" \
  -Q "RESTORE FILELISTONLY FROM DISK = N'$BackupFile' WITH FILE = 1, CHECKSUM;"
)
~~~

本项目预期两个逻辑文件：Project1Db（D，数据）、Project1Db_log（L，日志）。
数量／逻辑名称不同就先停止，重新安排每个文件的 MOVE，不能直接用下面模板。
FILELISTONLY 只列文件信息；VERIFYONLY 校验备份，都不等于实际恢复成功。

下面恢复到随机生成的新临时库，**不覆盖 Project1Db**：

~~~bash
(
set -eu
check_inputs
sudo -v
[ "$(systemctl show project1-api.service --property=ActiveState --value)" = "inactive" ] &&
[ "$(systemctl show project1-api.service --property=MainPID --value)" = "0" ] ||
    { printf 'STOP：API 未完全停止。\n'; exit 1; }

Root=/var/opt/mssql
[ "$(sudo readlink -e "$Root")" = "$Root" ] ||
    { printf 'STOP：恢复根目录不符合预期。\n'; exit 1; }
RestoreDir=$(sudo mktemp -d "$Root/project1-restore-check.XXXXXXXX")
Token=$(basename "$RestoreDir" | cut -d . -f 2)
[[ "$Token" =~ ^[A-Za-z0-9]{8}$ ]] ||
    { printf 'STOP：临时名称不符合预期。\n'; exit 1; }
RestoreDb="Project1Db_RestoreCheck_$Token"
sudo chown mssql:mssql "$RestoreDir"
sudo chmod 0700 "$RestoreDir"
printf '临时数据库：%s\n临时目录：%s\n' "$RestoreDb" "$RestoreDir"

env -i PATH=/usr/bin:/bin HOME="$HOME" LANG=C.UTF-8 \
  /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -d master \
  -C -b -I -x -l 15 -t 120 \
  -Q "
IF DB_ID(N'$RestoreDb') IS NOT NULL
BEGIN
    ;THROW 50000, 'Temporary database already exists. Stop.', 1;
END;
RESTORE DATABASE [$RestoreDb]
FROM DISK = N'$BackupFile'
WITH FILE = 1,
  MOVE N'Project1Db' TO N'$RestoreDir/restore-check.mdf',
  MOVE N'Project1Db_log' TO N'$RestoreDir/restore-check_log.ldf',
  CHECKSUM, RECOVERY, RESTRICTED_USER, STATS = 10;
"
)
~~~

RESTORE 创建临时库；MOVE 使用新文件位置；不加 REPLACE，不要求覆盖已有库。
看到 RESTORE DATABASE successfully processed 才继续。
将打印的真实临时库名填入 RestoreDb，然后在同一个 Ubuntu SSH 窗口检查：

~~~bash
RestoreDb='REPLACE_PRINTED_RESTORE_DATABASE_NAME'

(
set -eu
[[ "$RestoreDb" =~ ^Project1Db_RestoreCheck_[A-Za-z0-9]{8}$ ]] ||
    { printf 'STOP：先填写正确的临时库名。\n'; exit 1; }
env -i PATH=/usr/bin:/bin HOME="$HOME" LANG=C.UTF-8 \
  /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -d master \
  -C -b -I -x -l 15 -t 30 \
  -Q "ALTER DATABASE [$RestoreDb] SET READ_ONLY WITH NO_WAIT;"

env -i PATH=/usr/bin:/bin HOME="$HOME" LANG=C.UTF-8 \
  /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -d "$RestoreDb" \
  -C -b -I -x -l 15 -t 120 -W -w 2000 -s "|" \
  -Q "
SET NOCOUNT ON;
DBCC CHECKDB WITH NO_INFOMSGS;
SELECT DB_NAME() AS database_name,
       (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS applied_migrations,
       (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory) AS latest_migration,
       (SELECT COUNT_BIG(*) FROM dbo.Products) AS product_count;
"
)
~~~

先恢复，再用 -d 连接已存在的临时库检查，不在恢复那批 SQL 中写 USE，避免之前的批次错误。
这一段会有两次 SQL 密码提示：先从 master 设置临时库 READ_ONLY，再连接临时库 CHECKDB。
READ_ONLY 防止临时库被写入；NO_WAIT 遇到其他连接占用就报错而不一直等待，不强制中断其他人。
CHECKDB 检查一致性，不修复业务数据。
正常时 CHECKDB 通常没有额外输出；恢复库的迁移／产品数量应符合第 4 步的停写起点。
此步骤只证明恢复能力；复杂的新 SQL 仍需另外演练，不把备份能恢复当成迁移一定安全。

### 9. 【Ubuntu SSH】正式迁移：这一段真的修改 Project1Db

确认 SQL 已审阅、目标／待执行范围正确、备份已下载校验和恢复检查通过、仍保持停写、只有你操作。
如果备份后恢复写入，先刷新备份。本次没有 pending 就不执行旧 SQL，不改历史来制造 pending。
绝不能把恢复脚本中的临时库名换成正式库来演练。

~~~bash
(
set -eu
check_inputs
[ "$(systemctl show project1-api.service --property=ActiveState --value)" = "inactive" ] &&
[ "$(systemctl show project1-api.service --property=MainPID --value)" = "0" ] ||
    { printf 'STOP：API 未完全停止。\n'; exit 1; }

[ "$SqlFile" = "/var/lib/project1-migration-prepare/$PrepareToken/migrations.sql" ] ||
    { printf 'STOP：SQL 路径不符合预期。\n'; exit 1; }
Actual=$(sudo sha256sum -- "$SqlFile" | cut -d ' ' -f 1)
[ "$Actual" = "$SqlSha256" ] ||
    { printf 'STOP：SQL 哈希不一致。\n'; exit 1; }

sudo env -i PATH=/usr/bin:/bin HOME=/root LANG=C.UTF-8 \
  /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -d Project1Db \
  -C -b -I -x -X1 -f 65001 -l 15 -t 120 \
  -i "$SqlFile"
printf 'SQL 命令正常结束；API 保持停止，接下来验证。\n'
)
~~~

原文件通过 -i 直接执行，不拼接额外 SQL，不改写或现场生成另一份脚本。
报错、超时、断连就停：不重跑、不启动 API、不部署、不自动恢复、不删除记录。
-b 退出不代表整份 SQL 全部回滚，早先提交的部分可能已完成；先检查实际状态再安排修复。
即使命令正常结束，也要完成下一步验证。

### 10. 【Ubuntu SSH】验证正式结构和原有数据

这是 Products 新增列的模板：比较 Id、Note、CicdPracticeNote 是否与恢复库一致，不打印备注内容。
不是全部业务数据检查；改其他表／字段时，必须相应改变检查范围。

~~~bash
(
set -eu
check_inputs
[[ "$RestoreDb" =~ ^Project1Db_RestoreCheck_[A-Za-z0-9]{8}$ ]] ||
    { printf 'STOP：临时库名不正确。\n'; exit 1; }
env -i PATH=/usr/bin:/bin HOME="$HOME" LANG=C.UTF-8 \
  /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -d Project1Db \
  -C -b -I -x -l 15 -t 30 -W -w 2000 -s "|" \
  -Q "
SET NOCOUNT ON;
DECLARE @NotesUnchanged bit = 1;
IF EXISTS (
    SELECT Id, Note, CicdPracticeNote FROM dbo.Products
    EXCEPT
    SELECT Id, Note, CicdPracticeNote FROM [$RestoreDb].dbo.Products
) OR EXISTS (
    SELECT Id, Note, CicdPracticeNote FROM [$RestoreDb].dbo.Products
    EXCEPT
    SELECT Id, Note, CicdPracticeNote FROM dbo.Products
)
    SET @NotesUnchanged = 0;
SELECT DB_NAME() AS database_name,
       (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS applied_migrations,
       (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory) AS latest_migration,
       N'$ExpectedLatest' AS expected_latest_migration,
       (SELECT COUNT_BIG(*) FROM dbo.Products) AS product_count,
       @NotesUnchanged AS existing_notes_unchanged;
SELECT name AS column_name, TYPE_NAME(user_type_id) AS data_type, max_length, is_nullable
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'$NewColumn';
"
)
~~~

需要 Project1Db、latest_migration 等于 expected_latest_migration；
迁移数量为起点加本次 pending 数量；产品数量不变，existing_notes_unchanged=1。
新列类型／长度／可空性必须符合本次迁移；nvarchar(100) 的 max_length 为 200 字节。
结果不符合就停，不能只凭 SQL 命令成功而忽略验证。

### 11. 【Ubuntu SSH】启动兼容 API

仅适用于本指南的“旧 API 兼容新增可空列”方案：

~~~bash
sudo systemctl start project1-api.service
systemctl show project1-api.service \
  --property=ActiveState --property=SubState --property=MainPID
~~~

需要 active / running / MainPID 大于 0。进程运行不代表所有业务功能健康；
如果失败先查日志，不继续部署或重复迁移。

### 12. 【GitHub】部署同一 CI 版本的前后端

再次打开 Project1 manual deployment，使用第 3 步的同一 CI run ID 和完整 SHA，不是任意最新 CI。

| 输入 | 填写／勾选 |
| --- | --- |
| Use workflow from | main |
| ci_run_id / commit_sha | 与准备 SQL 所选 CI 相同 |
| deploy | 只勾选这一项 |
| check_migrations | 不勾选 |
| prepare_migrations | 不勾选 |

这次部署前后端并重启 API，**不会执行迁移**。
准备步骤灰色 Skipped 正常；应用部署 job 必须 Success。失败先检查该 job 日志。
代码回滚不等于数据库回滚，不能擅自删列或恢复覆盖正式库。

### 13. 【网页】最终验证并保留记录

Ctrl + F5 刷新产品页面：
填写／保存 → 刷新 → 再编辑确认值仍在 → 清空／保存 → 刷新为空且不报错。
同时检查原来的字段和相关业务功能。

保存本次 CI 来源、Summary、电脑／服务器备份位置和哈希、临时库名、执行时间和验证结果。
备份、SQL、操作记录先保留；清理临时库和备份要另行安排，不在本模板中自动删除。

### 命令和密码提示速查

| 命令／参数 | 简单意思 |
| --- | --- |
| migrations add / list / database update | 生成迁移／查看状态／更新指定数据库 |
| systemctl stop / start / show | 停止／启动／查看 API 服务 |
| sudo | Ubuntu 管理员权限，可能提示输入 Ubuntu 用户密码 |
| env -i | 使用干净环境，不继承其他配置；不代表只读 |
| sqlcmd -S / -U / -d | SQL 服务器地址／登录账号／数据库名 |
| sqlcmd -Q / -i | 执行命令内 SQL／执行 SQL 文件；是否修改数据取决于 SQL 内容 |
| sqlcmd -I | 打开 QUOTED_IDENTIFIER，注意大写 I |
| sqlcmd -C | 信任服务器证书，本 homelab 用法，不等于验证了证书身份 |
| sqlcmd -b / -l / -t | SQL 报错退出／登录超时秒数／查询超时秒数 |
| sqlcmd -x / -X1 / -f 65001 | 禁用脚本变量替换／禁用危险客户端命令并报错／UTF-8 输入输出 |
| sha256sum / Get-FileHash | Linux／PowerShell 的文件内容哈希检查 |
| scp | 通过 SSH 在电脑与服务器之间复制文件 |
| ( set -eu; ... ) | 出错或变量未设置时结束该操作块，不自动回滚已完成操作 |

[sudo] password 和 ssh / scp 密码属于 Ubuntu 用户 shunbin；
sqlcmd 的 Password: 是 SQL Server 的 sa 密码，不要混淆。
sa 是当前 homelab 演练身份，不交给应用或 GitHub；密码不写入 -P、Git、聊天或文档。
长期执行权限需另行审核。一次性工具安装／故障恢复不是每次迁移步骤：
见[准备工具说明](docs/MIGRATION_PREPARATION.md)、[手动迁移检查单](docs/MANUAL_MIGRATIONS.md)。

### 已完成的 StorageLocation 练习记录（仅留档，不是下次输入）

| 项目 | 2026-10-11 本次的值 |
| --- | --- |
| Commit SHA | ecd96ab74831e7ad1e577b2efa58ef325296b823 |
| CI run / attempt | 38123390962 / 1 |
| 准备完成时间 | 2026-10-11T08:21:11.923284+00:00 |
| 准备 token | 175629af310349048db5f02e9f571b9d |
| SQL 文件 | /var/lib/project1-migration-prepare/175629af310349048db5f02e9f571b9d/migrations.sql |
| SQL SHA-256 | cad95fedccd5e44b3a870a6c3992ec02a6889e5b288a0e7fb903604b3148070c |
| 服务器备份 | /var/opt/mssql/backup/project1-prepare-175629af310349048db5f02e9f571b9d/Project1Db.bak |
| Backup SHA-256 | f14ff2dfac471f60581f57268ebcc51eb26501b068fd838233886b307e859184 |
| Backup bytes | 13523456 |
| Ubuntu 下载副本 | /home/shunbin/project1-backup-download.lmzNu0dZ/Project1Db.bak |
| 电脑备份 | C:\Users\shun bin\Project1Backups\StorageLocation-2e45e1c2d5ca4f0d91827f9d1da4dcdb\Project1Db-before-StorageLocation.bak |
| 临时恢复库 | Project1Db_RestoreCheck_BYJv2fCb |
| 当时待执行、现已完成的迁移 | 20261011073715_AddProductStorageLocation |

当次已确认：备份下载校验、实际恢复、CHECKDB 成功；正式库由 18 到 19 个迁移；
StorageLocation 为 nvarchar(100) NULL；产品数量 1、原有备注一致；前后端部署及网页读写／清空成功。
这是完成记录，不代表下次操作前的实时状态。

参考：[EF 迁移管理与回退](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing)、
[SQL 脚本部署](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)、
[sqlcmd 参数](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility?view=sql-server-ver17)、
[SQL 批次 GO](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/sql-server-utilities-statements-go)。

## 删除 CicdPracticeNote 练习：先更新应用，再删数据库列

只删除 Products.CicdPracticeNote；保留 Note、StorageLocation 和其他产品数据。
删除列会清除该列中的值。Down 重新创建列不等于恢复值；需要恢复数据时应另行审核恢复方案。
完整数据库恢复还会覆盖备份之后的新写入，不能随意用旧备份覆盖正式库。
旧的 AddProductCicdPracticeNote migration 必须保留，不修改已上线的迁移历史。

与新增列不同，这次**先部署不依赖旧列的新前后端，再停止写入、备份和删列**。
这针对当前允许为空的练习字段；其他字段的约束、索引、外键和外部程序也要检查。

### A. 【电脑 PowerShell，仓库根目录】生成删除 migration

先完成代码移除和测试调整，再运行以下命令。它只生成文件，不执行数据库 SQL。

~~~powershell
dotnet tool run dotnet-ef -- migrations add RemoveProductCicdPracticeNote `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api `
  -- --environment Development
~~~

检查新 migration：Up 应只有 Products.CicdPracticeNote 的 DropColumn；
Down 应只加回 nvarchar(100) NULL 的同名列，不恢复原来的内容。
出现其他列删除、表重建或数据操作就先停下来审核。
快照由这个命令更新，不手动改旧 migration／Designer。

接着运行新增列指南第 2 步的本地测试。新增 migration 之前，SnapshotMatchesCurrentModel
会失败，这是阻止忘记生成迁移的检查，不能删掉或绕过这个检查再 push。
如果要在本地 Project1Db 执行 database update，要先备份：这次会真的删除本地该列和它的值。
仅生成文件、跑无数据库测试不会删除本地 Project1Db；CI 的 SQL 测试只使用一次性数据库。

### B. 【GitHub】CI 通过后，先只部署新应用

Push → PR CI 成功 → merge → main 的 push CI 成功。审核这个版本生成的 SQL 和 manifest。
记录这次完整 Commit SHA、CI run ID 和 attempt，不使用下面旧 StorageLocation 记录的值。

Project1 manual deployment → Run workflow → 选择 main，填写上述 SHA 和 run ID：

- deploy = true。
- prepare_migrations = false，check_migrations = false。

这次只更新应用，不执行删除 SQL。数据库仍有 CicdPracticeNote 是正常的。
确认网页不再显示这个字段，产品读取、新增、编辑、停用／启用、StorageLocation 保存正常。
确认没有旧 API 实例或其他程序依赖该列后，才进入下一阶段。

### C. 【Ubuntu SSH + GitHub】停止 API，再准备 SQL 和备份

按新增列指南第 4 步停止 API，确认 inactive、MainPID=0；停止其他写入，避免并行操作。
停止 API 不代表 SQL Server、后台任务或其他客户端自动停止写入。
在 GitHub 用同一个已验证 CI 版本再次 Run workflow：

- prepare_migrations = true。
- deploy = false，check_migrations = false。

准备成功后拿本次新 Summary。它只上传 SQL、创建备份，不会删列或部署应用。
待执行清单应只有新的 RemoveProductCicdPracticeNote；出现其他迁移先审核，不直接执行。

### D. 【Ubuntu SSH】填写本次值，下载备份并做临时恢复

下面取代新增列指南第 6 步的“变量设置及 check_inputs 函数”。
每次都从本次准备 Summary 填写，不从旧命令复制哈希或备份路径。
ExpectedLatest 是 Pending migrations 中完整名称，包括新生成的时间戳。

~~~bash
PrepareToken='REPLACE_32_CHARACTER_TOKEN'
SqlSha256='REPLACE_SQL_SHA256'
BackupSha256='REPLACE_BACKUP_SHA256'
BackupBytes='REPLACE_BACKUP_BYTES'
ExpectedLatest='REPLACE_FULL_REMOVE_MIGRATION_ID'
SqlFile="/var/lib/project1-migration-prepare/$PrepareToken/migrations.sql"
BackupFile="/var/opt/mssql/backup/project1-prepare-$PrepareToken/Project1Db.bak"

check_inputs() {
    if ! [[ "$PrepareToken" =~ ^[0-9a-f]{32}$ &&
            "$SqlSha256" =~ ^[0-9a-f]{64}$ &&
            "$BackupSha256" =~ ^[0-9a-f]{64}$ &&
            "$BackupBytes" =~ ^[1-9][0-9]*$ &&
            "$ExpectedLatest" =~ ^[0-9]{14}_RemoveProductCicdPracticeNote$ &&
            "$SqlFile" = "/var/lib/project1-migration-prepare/$PrepareToken/migrations.sql" &&
            "$BackupFile" = "/var/opt/mssql/backup/project1-prepare-$PrepareToken/Project1Db.bak" ]]; then
        printf 'STOP：先正确填写本次删列准备的 Summary 值。\n'
        return 1
    fi
}
check_inputs
~~~

保持同一 SSH 会话，复用第 6 步后半段的备份复制、第 7 步电脑下载、第 8 步临时恢复命令。
RestoreDb 是这次新打印的临时库名，不是正式 Project1Db。
下载副本要用本次 Backup SHA-256 和 Backup bytes 校验。
恢复演练应显示迁移前的状态，CicdPracticeNote 仍在；不要给临时库执行删除 SQL。
API／其他写入必须持续停止；如果写入恢复过，重新停止并准备新备份，更新全部 Summary 值。

### E. 【Ubuntu SSH】手动执行真实删列，再验证

准备、备份、恢复都成功且已明确确认删列后，执行新增列指南第 9 步的原始 SQL 命令。
它使用本次 SqlFile，并通过 -I 开启 QUOTED_IDENTIFIER；不要现场拼接修改 SQL。
报错或断连就停止，不盲目重跑、不启动 API、不自动恢复数据库。

**不要运行新增列指南第 10 步**：它查询已删除的 CicdPracticeNote，会报错。
使用下面的删列验证：比较保留的全部 Products 字段，确认迁移记录、删除结果和保留列结构。

~~~bash
(
set -eu
check_inputs
[[ "$RestoreDb" =~ ^Project1Db_RestoreCheck_[A-Za-z0-9]{8}$ ]] ||
    { printf 'STOP：先填写本次临时恢复数据库名称。\n'; exit 1; }
env -i PATH=/usr/bin:/bin HOME="$HOME" LANG=C.UTF-8 \
  /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -d Project1Db \
  -C -b -I -x -l 15 -t 30 -W -w 2000 -s "|" \
  -Q "
SET NOCOUNT ON;
IF DB_NAME() <> N'Project1Db'
    THROW 50000, 'Wrong target database.', 1;
IF EXISTS (
    SELECT Id, Code, Name, Description, Note, StorageLocation, ProductCategoryId,
           UnitOfMeasureId, DefaultUnitPrice, ReorderLevel, IsActive, CreatedAtUtc, UpdatedAtUtc
    FROM dbo.Products
    EXCEPT
    SELECT Id, Code, Name, Description, Note, StorageLocation, ProductCategoryId,
           UnitOfMeasureId, DefaultUnitPrice, ReorderLevel, IsActive, CreatedAtUtc, UpdatedAtUtc
    FROM [$RestoreDb].dbo.Products
) OR EXISTS (
    SELECT Id, Code, Name, Description, Note, StorageLocation, ProductCategoryId,
           UnitOfMeasureId, DefaultUnitPrice, ReorderLevel, IsActive, CreatedAtUtc, UpdatedAtUtc
    FROM [$RestoreDb].dbo.Products
    EXCEPT
    SELECT Id, Code, Name, Description, Note, StorageLocation, ProductCategoryId,
           UnitOfMeasureId, DefaultUnitPrice, ReorderLevel, IsActive, CreatedAtUtc, UpdatedAtUtc
    FROM dbo.Products
)
    THROW 50000, 'Remaining product data differs from backup.', 1;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'CicdPracticeNote')
    THROW 50000, 'Retired column still exists.', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Products') AND
    ((name = N'Note' AND TYPE_NAME(user_type_id) = N'nvarchar' AND max_length = -1 AND is_nullable = 1) OR
     (name = N'StorageLocation' AND TYPE_NAME(user_type_id) = N'nvarchar' AND max_length = 200 AND is_nullable = 1))) <> 2
    THROW 50000, 'Remaining column definitions differ.', 1;
IF (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory) <> N'$ExpectedLatest' OR
   (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <>
   (SELECT COUNT(*) + 1 FROM [$RestoreDb].dbo.__EFMigrationsHistory)
    THROW 50000, 'Migration history differs from expected single removal.', 1;
SELECT DB_NAME() AS database_name,
       (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS applied_migrations,
       (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory) AS latest_migration,
       (SELECT COUNT_BIG(*) FROM dbo.Products) AS product_count,
       0 AS practice_column_exists, 1 AS remaining_product_data_unchanged;
"
)
~~~

此比较只针对 Products，不是整库所有表的验证。验证失败时 API 保持停止，先排查。

### F. 【Ubuntu SSH + 浏览器】启动已更新的 API，检查页面

验证通过后，用第 11 步启动 API、检查 active/running，再在网页编辑保存／刷新确认。
新应用已在 B 阶段部署，不需要照新增列流程再部署一次。
删列之后，**不要回滚到依赖 CicdPracticeNote 的旧应用**；应用回滚不会把数据库列和原值恢复。
之后再新增其他列时，也要按实际保留字段调整数据比较；旧新增列指南第 10 步仍引用 CicdPracticeNote，不能原样复用。
保留备份、恢复库、SQL 和 Summary，本指南不自动删除它们。
