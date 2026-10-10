# 生产迁移 CD：仓库接入完成，尚未安装或授权

本版本增加固定生产宿主、服务器自行验证 GitHub 产物、独立备份验证、管理员批准登记和手动执行选项。
**合并代码不会安装 Ubuntu 文件、建立账号、授予权限、登记批准或执行生产 SQL。**
没有自动迁移、启动时迁移、自动重试、失败后恢复、账本清空或重新发放批准功能。
安装与数据库权限是下一次必须单独确认的管理操作，不要直接把 CI 测试宿主加入 sudoers。

## 支持范围

目标固定为 `homelab-server / Project1Db / tcp:127.0.0.1,1433`，SQL Server 主版本 17。
只支持待执行迁移恰好为 `20261009164508_AddProductExampleAttr` 的 nullable `Products.Note` 升级。
注册及执行前 Note 必须不存在，执行后必须是 nullable `nvarchar(max)`；历史必须与精确产物一致。
已应用 Note、待执行为空或存在其他待执行迁移时拒绝，不重新应用，不自动调整权限。
新迁移必须另行审阅 SQL、定义目标结构/权限/备份策略并增加测试。

## 权限与可信入口

- 既有 `project1_migrate` 和 `/etc/project1-migration/password` 保持只读，本功能不读取它们。
- 新执行身份固定为 `project1_execute`，没有账号创建脚本，也没有使用 `sa` 的回退路径。
  所需对象权限是 Products ALTER、历史 SELECT/INSERT；宿主拒绝被检查到的 broad 权限和业务 DML 权限。
  ALTER 是表级权限，不是“只能添加 Note”；必须审核账户角色、public 权限、所有权与确切 SQL。
- 独立验证身份固定为 `project1_backup_verify`，只在固定 master 连接执行 HEADERONLY/VERIFYONLY。
  SQL Server 对读取备份信息/验证要求 CREATE DATABASE 权限，**这不是一个仅能验证备份的细粒度权限**。
  因此它必须单独审批、保密，不能授予执行账号或 runner；当前没有授予该权限。
  固定宿主要求 master 的 CREATE DATABASE，拒绝所检查的 sysadmin/securityadmin/dbcreator、db_owner、
  CONTROL SERVER、ALTER ANY LOGIN/ALTER ANY DATABASE；这仍不能替代完整的独立权限审计。
- 部署用户只能执行 `project1-migration-execute ID` 或只读 `--describe ID`。
  ID 必须是 32 位小写十六进制；没有 SQL 路径、连接地址、密码、模块或 JSON 替换参数。
- 管理员单独使用 `project1-migration-admin initialize / inspect / register ID`。
  admin 包装器绝不能加入部署用户 sudoers；部署用户没有登记批准、修改账本或备份的能力。

## 安装审阅清单（不是自动安装脚本）

必须先审阅 PR 并等待 main CI 成功。之后在独立管理步骤确认以下目标不存在/不会覆盖其他安装：

| 位置 | 所有者与权限 | 内容 |
| --- | --- | --- |
| `/usr/local/libexec/project1-migration` | root:root 0755 | 只放审阅过的运行依赖 `.py`，文件 0644；禁止链接和非 root 写入 |
| `/usr/local/libexec/project1-migration-host` | root:root 0755 | 审阅版本的 Linux x64 .NET 发布文件及依赖，DLL/JSON 0644，目录 0755 |
| `/usr/local/sbin/project1-migration-execute` | root:root 0755 | 固定隔离 Python 包装器 |
| `/usr/local/sbin/project1-migration-admin` | root:root 0755 | 仅管理员包装器，不授予部署用户 |
| `/etc/project1-migration-execution` | root:root 0700 | 独立凭据与启用开关 |
| `/etc/project1-migration-execution/approvals` | root:root 0700 | 管理员自行准备的 `ID.json`，0600 |
| `/var/lib/project1-migration-ledger` | root:root 0700 | 显式初始化产生的 lock/ledger 文件 0600 |
| `/var/lib/project1-migration-backups` | root:mssql 0750 | 仅 root 可写，mssql 仅能读取的备份副本 |

Python 运行文件清单：`production_entry.py`, `production_migration.py`, `migration_execution.py`,
`migration_target.py`, `migration_approval.py`, `migration_approval_ledger.py`, `migration_package.py`,
`precheck_migrations.py`, `verify_migration_artifact.py`, `verify_ci_artifacts.py`, `project1_deploy.py`,
`deploy_verified.py`。不要复制 tests、`__pycache__`、上传产物中的 Python 或整个用户可写仓库。
隔离入口先验证安装目录和所有 Python 文件，再开放固定目录导入。

仅在开发机生成可审阅的 Linux 发布输出，不进行安装：

```powershell
dotnet publish scripts/deploy/dotnet/Project1.MigrationHost/Project1.MigrationHost.csproj `
  --configuration Release --runtime linux-x64 --self-contained false `
  --output artifacts/migration-host
```

服务器需要 root 管理的 .NET 10 runtime；不得从部署用户目录加载 DLL、runtime、native library 或配置。
发布文件和 Python 文件必须逐个校验 SHA-256，在 root 私有目录审阅后安装，不能从上传目录执行。
`project1-migration-execute.sudoers` 仅是待审阅模板，需要 sudo >= 1.9.10 和 `visudo -cf`/`visudo -c` 校验。
应在正式安装前使用 Ubuntu 隔离测试验证：合法 ID/describe 可执行，多参数、路径、admin 和注入均被拒绝。

私有文件（均 root:root 0600，单链接且不能是符号链接）：

- `execution-password`：只属于 `project1_execute`，格式 `P1!` 加 64 位小写十六进制，再换行。
- `verifier-password`：独立的 `project1_backup_verify` 密码，同样格式；不能复用执行密码。
- `github-token`：仅固定仓库 Actions 读取权限的独立凭据；不要复制 GITHUB_TOKEN 临时值或应用 secrets。
- `enabled`：明确批准安装与权限之后才创建，精确内容为 `ENABLE_REVIEWED_PRODUCT_NOTE_EXECUTION_V1` 加换行。
  缺失或错误时执行/注册拒绝；inspect/describe 仍可读账本。此文件不是单次迁移批准。

不要在终端日志、GitHub secrets 或命令参数中打印/传递数据库密码。
Python 到 .NET 的凭据只通过私有 stdin；子进程使用固定路径和清理后的环境，不继承外部 .NET 配置。
实际执行身份和数据库状态在持锁 SQL 会话内再次验证，连接不池化、不自动重连。

## 管理员准备和登记批准

1. 先使用原只读 precheck 核对待执行范围。如果 pending=0，停止，不制造测试迁移、不回退生产历史。
2. 审阅选定 main/push CI 的原始 SQL 和部署兼容性；确认维护窗口、业务写入控制和人工恢复方案。
3. 管理员独立完成新鲜 COPY_ONLY/CHECKSUM 全库备份，单个备份集，不追加、无损坏。
   另行完成恢复演练；VERIFYONLY 不是完整恢复演练，也不会替你恢复数据库。
4. 将实际备份复制到固定目录 `SHA256.bak`：root:mssql 0640、单链接，目录和祖先只能 root 写。
   不能直接使用 `/var/opt/mssql` 下 mssql 可写路径，否则无法保证 SQL 验证与 OS 哈希读的是相同不可变文件。
5. 从真实 HEADERONLY 读取 `BackupFinishDate` 和 `TimeZone`。当前配置仅支持原始备份时区与服务器时区均 UTC；
   原始时区未知或非 UTC 会拒绝，不能拿电脑上的“校验完成时间”替代备份完成时间。
6. 按 [批准格式](MIGRATION_APPROVAL.md) 自行审阅并准备固定 `approvals/ID.json`；字段、哈希、时间窗口均检查。
   注册使用真实执行会话身份 `project1_execute`；旧离线 CLI 默认只读身份仍不变，不能替代登记入口。
7. 管理员显式 initialize（仅首次、拒绝已有/部分账本），然后 register ID。
   register 自行在线下载固定仓库精确 CI ZIP，核对 attempt/哈希、实时历史、目标结构及实际备份，**不执行迁移 SQL**。
   备份哈希在 SQL 验证前后读取；备份文件的 inode、大小、时间、权限也必须一致。

SQL 宿主根据备份头独立确定完成时间；仅有批准 JSON 的 `verified: true` 无法通过。
账本不是灾难恢复或恶意 root 防护：root 可以回退账本，恢复时必须结合真实数据库状态与审计。

## 手动工作流

使用现有 **Project1 manual deployment**，workflow 分支必须 main：

- 指定精确成功 main/push CI run ID 与完整 commit SHA。
- `deploy=false`，`check_migrations=false`，只勾选 `execute_migrations`。
- 输入管理员已登记的 `migration_approval_id`；勾选本身不产生服务器批准。
- runner 先重新验证产物，再通过 pinned OpenSSH 查询 `--describe ID`，核对全部来源/SQL哈希绑定。
  与所选 CI 不一致时不会提交执行命令。只发送编号，不上传 SQL、批准 JSON、密码或可执行代码。
- 服务器重新下载并独立在线验证批准绑定的 CI 产物，持锁检查备份与历史，持久领取批准后才执行同一 SQL 字节。
  完成后确认结构/历史并持久记录结果；不会部署应用或重启服务。

保持原 workflow-main Tailscale OIDC 身份，不新增 environment 以免无意改变现有网络授权。
人工批准边界是服务器管理员的不可由部署用户写入的账本，而不是假定 GitHub environment 自动启用了 required reviewers。
原应用部署和只读预检查仍是互斥的独立模式，三个 server 模式默认全部关闭；同一 concurrency 组不取消运行中任务。

## 失败和验证边界

领取之后任何失败/中断都不允许自动重试；claimed/failed/interrupted 阻止其他批准。
SSH 输出丢失或超时不能解释为 SQL 没运行；必须由管理员检查账本、真实历史/结构和备份。
没有 clear/rearm/recover 命令；不撤销此前已提交分段，不自动恢复数据库。
管理员检查路径/权限、恢复方案和维护窗口后才能选择下一步。

本地测试仅验证固定目标、参数拒绝、来源重查、备份哈希前后比对、登记/执行顺序、脱敏及 transport 不重试。
GitHub 临时 SQL 用例复用真实宿主的 BackupInspector，验证 HEADERONLY、原始 UTC 时间与 CHECKSUM VERIFYONLY。
**固定生产 stdio 宿主/完整 sudo 安装需要 Ubuntu 隔离环境验收，未在本机或生产执行。**
CI 必须通过后才讨论服务器安装，不能将“本地编译通过”说成“生产已启用”。

参考：[HEADERONLY 结果与时区](https://learn.microsoft.com/en-us/sql/t-sql/statements/restore-statements-headeronly-transact-sql?view=sql-server-ver17)、
[VERIFYONLY 权限与限制](https://learn.microsoft.com/en-us/sql/t-sql/statements/restore-statements-verifyonly-transact-sql?view=sql-server-ver17)。
