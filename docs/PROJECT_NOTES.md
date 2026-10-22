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

- [UI Test Guide](UI_TEST_GUIDE.md) — complete browser walkthrough using every business role.
- [Workflow Engine](WORKFLOW_ENGINE.md) — templates, instances, actions, authorization, and module integration.
- [Architecture](ARCHITECTURE.md) — frontend/backend structure, authentication, email, inventory, validation, and testing.
- [Docker 自动部署：从零设置指南](deployment/docker-cicd.md) — 全新 Ubuntu 的 Docker / SQL Server / Nginx、GitHub / GHCR、Tailscale OIDC / SSH、首次部署及日常迁移。
- [API 启动自动迁移](deployment/api-startup-migrations.md) — 本地生成 migration，API 启动应用 pending migrations。

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

See the [UI Test Guide](UI_TEST_GUIDE.md#demo-accounts-and-role-responsibilities) for page-level responsibilities and the complete role-by-role process.

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

For the complete field-by-field walkthrough, use the [UI Test Guide](UI_TEST_GUIDE.md). The shortest full demonstration is:

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

### Docker CI/CD

PRs run backend, frontend and migration tests on disposable GitHub-hosted runners.
A push to main builds two images in GHCR. With DOCKER_DEPLOY_ENABLED=true,
the same workflow uses Tailscale/SSH to pull those exact image digests on Ubuntu.

- Workflow: [.github/workflows/ci.yml](../.github/workflows/ci.yml)
- Setup and daily use: [Docker 自动部署](deployment/docker-cicd.md)
- Database changes: [启动自动迁移](deployment/api-startup-migrations.md)

Database contents, server secrets and old server directories are not deleted by CI.
Old manual ZIP/TAR deployment and production SQL preparation flows have been retired.

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
- For ordinary local `dotnet run`, apply pending migrations manually; the production Docker API applies them automatically when enabled.
- The backend is the final authorization authority; hidden frontend buttons are not security controls.
- Use deactivation for master data referenced by historical records instead of destructive deletion.
- Use UTC timestamps in stored audit records and convert only for display.
- Install `fonts-dejavu-core` on Ubuntu so Purchase Order PDF generation has a supported font.

For implementation details, see [Architecture](ARCHITECTURE.md).


## 本地 Docker 练习：Vue + API + 独立 SQL Server

**这里所有命令都在电脑 PowerShell 的仓库根目录执行，不是在 Ubuntu SSH。**
这是一套新的练习环境，不连接 Windows 原有的 `Project1Db`，也不连接 Ubuntu。
现在可以使用 Vue 容器：Node 只负责编译，Nginx 负责运行时提供网页和转发 API 请求。
已经完成 API／数据库练习的用户，添加 Vue 时从步骤 6 开始，添加邮件捕获时从步骤 6A
开始；不要重新生成密码或重复迁移。Mailpit 只保存测试邮件，不发送到真实邮箱。

### 当前验证记录（2026-10-12／2026-10-13）

- API 和迁移工具镜像构建成功；练习库已手动应用现有 migrations。
- 使用电脑 Vite 前端验证了登录、分类／单位／产品创建和产品修改。
- 停止并重建 API／SQL Server 容器后，测试产品及 `Docker-Shelf-B2` 仍保留。
- Vue 镜像构建和类型检查通过，223 个前端单元测试通过。
- 临时模拟 API 检查通过：Nginx 页面刷新、API 路径／参数／请求体／令牌转发、
  API 错误状态保留、PDF 响应转发。临时测试不连接练习库或正式库。
- 用户已在 `http://localhost:8081/products` 验证登录、原产品显示及页面刷新。
- 2026-10-13：Mailpit 和更新 SMTP 配置后的 API 均 Healthy；从 API 容器发送的
  一封独立测试邮件已被本地捕获，主题和正文检查通过，非允许 Host 请求被拒绝。
  SQL Server 容器未重建，登录页源码未修改，没有自动重试采购订单邮件。
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
| 电脑浏览器查看 Mailpit 捕获的测试邮件 | `http://localhost:8026` |
| 电脑访问练习 API | `http://localhost:5166` |
| Nginx 容器转发 API 请求 | `http://api:8080`（保留 `/api/...` 路径） |
| 电脑使用 SSMS 连接练习 SQL Server（SQL 登录） | `localhost,14334` |
| API 容器连接数据库 | `sqlserver,1433` |
| API 容器发送测试邮件 | `mailpit:1025`（不开放电脑 SMTP 端口） |
| 练习数据库 | `Project1DockerPracticeDb` |
| Compose 项目 | `project1-docker-practice` |

容器里的 `localhost` 是容器自己，不是你的电脑。API 使用 Compose 的服务名
`sqlserver` 连接数据库。四个电脑端口都只绑定 `127.0.0.1`，不对局域网开放。
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
Compose 会先确认依赖的 API、SQL Server 和 Mailpit 健康；若它们已停止，会一并启动。
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

本轮不测试真实邮箱投递。API 的 SMTP 连接 Compose 内的 `mailpit:1025`，
测试邮件仅保存在 Mailpit，收件人即使填写真实邮箱也不会收到本次测试邮件。
不配置 Gmail／正式 SMTP 密码，不启用 Mailpit relay、forwarding 或外部 webhook。
PDF 字体已包含在 API 镜像中，但 PDF 业务验证仍需要对应的练习数据，
不能仅凭构建成功或模拟 PDF 转发成功就认为已经验证。

### 6A. 使用 Mailpit 测试邮件和 PDF 附件

所有命令仍在**电脑 PowerShell、仓库根目录**执行，不是在 Ubuntu SSH。
只更新本机 Compose 邮件配置；不生成／运行 migration，不删除数据库 volume，
不修改 `.env.docker.local`，也不需要重新构建 API 或 Vue 镜像。

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml config --quiet
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait mailpit
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait api
docker compose --env-file .env.docker.local -f compose.local.yml ps
```

- `config --quiet`：检查配置，不显示密码。
- `up ... mailpit`：下载固定版本镜像、创建本地捕获服务，等待其健康检查通过。
- `up ... api`：应用新的 SMTP 地址。已有 API 容器会因配置变化而重建，短暂中断请求；
  SQL Server 数据卷不变。普通 `restart api` 不会应用修改后的 Compose 环境变量。
- `ps`：查看 Mailpit、API 和数据库状态。SMTP 不映射到电脑；只有邮件网页
  映射为 `127.0.0.1:8026:8025`。如果 8026 已被占用，先排查，不停止其他服务。

浏览器打开 `http://localhost:8026`。健康检查只确认服务准备就绪，不代表业务邮件已成功。
更新 API 后不要再使用 `127.0.0.1:1025`；它会连接 API 容器自身，而不是 Mailpit。

先验证已有失败记录，不重复创建采购订单：

1. 在 `http://localhost:8081/email-records` 找到失败的测试邮件，点 **View**。
2. 检查收件人、邮件内容、错误和 PDF 附件；只操作练习数据。
3. Mailpit 和 API 均 Healthy 后，点一次 **Retry email**，等待后台处理再刷新。
   原记录从 Failed 变成 Pending，再变成 Sent；失败记录不会因重启 API 自动重试。
4. 在 Mailpit 网页找到相同主题，检查正文，并打开／下载 PDF 附件，确认内容正常。
   应用中的 Sent 在这里仅表示 Mailpit 已接收，不表示真实收件人的邮箱已收到。
5. 若仍 Failed，查看 **View** 中的最新错误；不要反复点 Retry 或重新运行 migration。

Mailpit 使用独立的 `mailpit-data` volume 保留捕获邮件，普通 `down` 不删除它。
默认最多保留 500 封，旧邮件可能被自动清理；这不是正式邮件归档或数据库备份。
邮件正文和附件可能包含敏感信息，页面仅限本机，不提交捕获邮件或截图中的私密数据。
这套无 SMTP 登录／TLS 的配置仅用于本机测试，不能直接用来配置正式公网发信。

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
docker compose --env-file .env.docker.local -f compose.local.yml logs --tail 80 mailpit
```

只运行与你的问题有关的查看命令。日志也可能包含测试业务数据，分享前先检查。
数据库健康检查通过但 API 启动报找不到表：确认步骤 4 已成功。
前端出现 502：检查 API 状态和前端日志；不要重新迁移或删除 volume 来解决代理问题。
邮件 Failed：先按步骤 6A 应用 Mailpit 配置，然后检查记录中的最新错误；
Mailpit 中有邮件但 Gmail 中没有是本地捕获的正常结果，不是外部邮箱投递成功。
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
[非 root Nginx 镜像](https://github.com/nginx/docker-nginx-unprivileged)、
[Mailpit Docker 配置](https://mailpit.axllent.org/docs/install/docker/)、
[Mailpit 健康检查](https://mailpit.axllent.org/docs/integration/healthcheck/)。

## 本机 Docker 连接原 Windows SQL Server，并使用 Gmail

**这是另一套配置，不是步骤 6A 的 Mailpit 练习。网页操作会直接修改原来的 `Project1Db`。**
只运行 Vue／API 容器，不移动、恢复、合并或重新迁移数据库。原有
`compose.local.yml`、练习数据库和 Mailpit volume 都保留；两个 Compose 文件不要合并运行。

| 配置 | 隔离练习 | 原 Windows 数据库 |
| --- | --- | --- |
| Compose 文件 | `compose.local.yml` | `compose.host-sql.yml` |
| 私有配置 | `.env.docker.local` | `.env.docker.host-sql.local` |
| 项目名 | `project1-docker-practice` | `project1-host-sql-local` |
| 网页 | `http://localhost:8081` | `http://localhost:8082` |
| API | `http://localhost:5166` | `http://localhost:5167` |
| 数据库 | `Project1DockerPracticeDb`，容器内 | `Project1Db`，Windows 的 `BIN/MSSQLSERVER` |
| 邮件 | Mailpit 本地捕获 | Gmail SMTP，真实投递 |

### 已检查的起点（2026-10-14）

- `BIN` 的默认 SQL 实例为 `MSSQLSERVER`，注册配置为 `MSSQL16.MSSQLSERVER`。
- `Project1Db` 在线可写；20 个迁移 ID 与代码完全匹配，最新为
  `20261011094217_RemoveProductCicdPracticeNote`。无需新增或重复执行 migration。
- 当前是 Windows-only 登录，TCP 尚未启用；没有监听 1433。
- 原库当时有 5 条 Sent 邮件、没有 Pending 邮件。启动前必须重新检查 Pending，
  因为后台 worker 会发送排队邮件，不能把旧的业务邮件当测试邮件自动发出。
- 已创建 COPY_ONLY／CHECKSUM 备份，且 `RESTORE VERIFYONLY ... WITH CHECKSUM`
  通过，备份位于 SQL 默认 Backup 目录。普通终端没有读取文件的权限，未生成 SHA-256；
  不为此放宽备份 ACL。这是备份校验，不是实际恢复演练。
- 初次准备时终端不是 Windows 管理员，因此当时只备份和准备文件，未更改服务器设置。

### 当前执行记录（2026-10-14）

- 用户已在管理员 PowerShell 成功执行初始化脚本：备份校验通过、混合登录和
  loopback TCP 1433 已开启、受限 SQL 账号已创建；Windows 登录保持可用。
- `Test-HostSqlConnection.ps1` 已实际通过 Docker 到 Windows 的连接和账号权限检查，
  确认目标为 `Project1Db`，不需要扩大监听地址或修改防火墙。
- 用户已在本机填写 Gmail 配置；私有文件／初始化状态均被 Git 忽略，未输出密码。
- 新项目 `project1-host-sql-local` 的 API 和 Vue 镜像构建成功，两个应用容器均 Healthy。
- `localhost:8082` 首页、登录页和 Products 直接链接返回网页；经 Nginx 访问
  `/api/auth/me` 在未登录时正确返回 401，API startup endpoint 正常。
- SQL 会话元数据确认运行中的新 API 使用专用账号连接原 `Project1Db`。
  启动前后产品／用户／邮件记录数为 11／11／5，Pending 为 0，迁移 ID 未改变。
- 后续用户已确认：在 `localhost:8082` 实际登录，并成功发送、收到 Gmail 邮件。
  PDF 附件内容仍待用户单独验证；没有由助手自动发送邮件。
- 本轮构建发现 `System.Security.Cryptography.Pkcs` 9.0.4 的 NU1902／NU1903 安全警告，
  对应 [微软公告（中等）](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) 和
  [微软公告（高）](https://github.com/advisories/GHSA-846f-f764-76mx)。2026-10-19 已在源码中
  显式固定同主版本补丁 `9.0.21`，API 测试和离线迁移测试通过，NuGet 当前审计未报告
  API 项目的已知漏洞。旧镜像／运行中的服务不会因此自动更新，需重新构建并部署。

### 1. 先审阅管理员初始化脚本

查看 `scripts/docker/Initialize-HostSql.ps1`，不带参数只显示说明、不修改设置：

```powershell
& .\scripts\docker\Initialize-HostSql.ps1
```

确认没有其他应用正在写入原库，结束原有连接该库的 `dotnet run`／API，避免两套
API 同时处理邮件队列。在 SSMS 中可继续用 Windows Authentication 管理，但先结束正在执行的查询。

**下一条仅在电脑“以管理员身份运行”的 PowerShell 执行，并且已确认允许重启此 SQL 实例：**

```powershell
Set-Location -LiteralPath 'C:\Users\shun bin\source\repos\Project1'
& .\scripts\docker\Initialize-HostSql.ps1 -Apply
```

脚本限制在已审阅的 `BIN/MSSQLSERVER`，执行前检查权限、迁移记录、Pending 邮件、
端口和账号冲突；会重新生成一份不覆盖旧文件的 COPY_ONLY 备份并校验。
然后生成受 ACL 保护的私有配置（不显示密码），创建 `project1_docker_local` 登录和
数据库用户，只授予 `db_datareader`／`db_datawriter`，不授予 `sysadmin`、`db_owner` 或表结构修改权限。
它开启混合验证，只启用 `127.0.0.1:1433` 的 TCP 监听，并重启一次 `MSSQLSERVER`。
不启用 `sa`，不关闭防火墙，不开放局域网／公网规则，其他 SQL Express 实例不修改。
SSMS 原来的 Windows 登录保持可用；此实例的所有连接会短暂中断。

私有 `.docker.host-sql-state.local.json` 保存原登录模式／TCP 配置和备份路径，
不包含密码，供失败时审阅恢复。脚本拒绝覆盖已有账号或已有状态文件，不能当常规启动命令重复运行。
**如果任何一步失败，保留私有文件和状态文件，停止并反馈错误；不要删除文件、
重置账号或把监听扩大到全部网卡来强行继续。** 如果脚本执行被系统策略阻止，先反馈错误，不自动关闭策略。

### 2. 只读验证 Docker 到 Windows 的连接

回到普通电脑 PowerShell 的仓库根目录执行：

```powershell
& .\scripts\docker\Test-HostSqlConnection.ps1
```

它使用 SQL Server 镜像中的 `sqlcmd` 工具启动一次临时诊断容器，**不启动 SQL Server
服务**；只把 SQL 密码通过环境变量交给工具，不传 Gmail 密码，不把密码放进命令行。
连接 `host.docker.internal:1433`，检查目标库、账号权限、迁移 ID 和 Pending 邮件。
诊断后删除临时容器并恢复当前终端原有的 `SQLCMDPASSWORD`。

只有成功且目标库确实为 `Project1Db`，才继续。Docker Desktop 是否能访问仅绑定
loopback 的宿主服务需要实际验证；若失败，不自动改为全网卡监听，也不开放防火墙，先反馈错误。

### 3. 在本机填写 Gmail 凭据

在 VS Code 用 Ctrl+O 打开仓库根目录的 `.env.docker.host-sql.local`（它已被 Git 忽略）。
**不要复制覆盖原来的 `.env.docker.local`。** 管理员脚本已生成前三项，不要修改或重新生成：

- `HOST_SQL_PASSWORD`：与新建的 SQL 登录对应。
- `JWT_SIGNING_KEY`：新环境使用独立签名密钥和 issuer/audience。
- `NEW_USER_PASSWORD`：仅供管理员在网页创建新用户时使用；不会重置原账号。

只在本机填两项：

- `GMAIL_ADDRESS`：真实发信 Gmail 完整地址；同时用于 SMTP 登录和新邮件的 From。
- `GMAIL_APP_PASSWORD`：Google App Password，去掉显示分组空格；不是 Google 普通登录密码。

Google App Password 需要两步验证，有些账户不提供此选项；没有此选项时先说明情况，
不要绕过账号保护。不把真实凭据提交 Git、放进 Dockerfile 或发送到聊天。
Gmail 使用 `smtp.gmail.com:587` 和 STARTTLS；此环境会真正发送业务邮件，不是 Mailpit。

### 4. 检查、构建，再启动

确认原有 Windows API 已停止，并且步骤 2 刚刚检查过没有 Pending 邮件；下一条
`config --quiet` 只是格式检查，不验证 Gmail 登录。如果私有值为空，配置会直接拒绝启动。

```powershell
docker compose --env-file .env.docker.host-sql.local -f compose.host-sql.yml config --quiet
docker compose --env-file .env.docker.host-sql.local -f compose.host-sql.yml build api frontend
& .\scripts\docker\Test-HostSqlConnection.ps1
docker compose --env-file .env.docker.host-sql.local -f compose.host-sql.yml up -d --wait api
docker compose --env-file .env.docker.host-sql.local -f compose.host-sql.yml up -d --wait frontend
docker compose --env-file .env.docker.host-sql.local -f compose.host-sql.yml ps
```

每条成功才继续。`build` 只制作镜像，`up` 才启动容器；此文件没有 sqlserver、migrate、
mailpit 服务，也没有数据库 volume。API 不执行 migration，但启动时仍会补齐缺失的
角色、邮件模板／工作流数据。Demo 用户初始化已关闭，不创建或重置演示账号。
端口 8082／5167 若被占用，先排查，不停止其他应用。

打开 `http://localhost:8082`，使用**原 `Project1Db` 中现有账号的密码**登录，
不是隔离练习环境的 `DEMO_PASSWORD`。先只查看数据；新建、修改和发信都会影响原库。
邮件测试只对自己控制的收件邮箱执行一次，不重复发送旧供应商订单。
旧邮件记录的 From／正文／附件是历史快照；修改 SMTP 不会自动改变旧记录。
先创建一封明确的测试邮件并检查其 From，再确认 Gmail 收到邮件和 PDF 附件。
SMTP 返回 Sent 仅代表服务器接受，仍需检查收件箱／垃圾邮件；不要据此断言收件人一定收到。

停止新环境只移除应用容器／网络，不删除 Windows 数据库：

```powershell
docker compose --env-file .env.docker.host-sql.local -f compose.host-sql.yml down
```

这不会回滚已经对原库进行的写入或发出的真实邮件，也不会自动恢复 Windows SQL 设置。
出现问题先停新 API、保留备份／私有状态，审阅后再决定恢复，不自动删除用户／还原原库。
此配置仅适用于本机 Docker Desktop，Ubuntu 的数据库地址、凭据、TLS 和部署配置需另行准备，
不能把 Windows 私有文件或 `BIN` 初始化脚本直接用于服务器／CI。

参考：[Docker Desktop 访问宿主服务](https://docs.docker.com/desktop/features/networking/networking-how-tos/)、
[SQL 登录模式](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/change-server-authentication-mode)、
[SQL TCP 配置](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/configure-a-server-to-listen-on-a-specific-tcp-port)、
[Gmail SMTP 设置](https://support.google.com/mail/answer/7104828)、
[Google App Password](https://support.google.com/mail/answer/185833)。


## 本地已验证的启动自动迁移

compose.host-sql.yml 已开启 Database__MigrateOnStartup，连接本机 Windows Project1Db。
生成 migration 后重建 API 镜像，首次启动会应用 pending migrations；再次启动会跳过已完成的迁移。
不要为了“重做 CI/CD”删除已经执行过的 migration、ModelSnapshot 或数据库迁移历史。

自动部署不会使用这份 Windows 配置；Ubuntu 仅使用服务器自己的私密配置文件。
