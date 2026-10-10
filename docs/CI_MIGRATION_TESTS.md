# CI 数据库迁移验证

采用 **CI 验证 + 手动迁移**：CI 生成并测试 SQL，生产迁移由管理员执行。
不再包含生产执行器、批准账本、备份协调或 Ubuntu 宿主安装验收。
既有 EF 迁移文件、模型和数据库历史保持不变。

## 保留的检查

这些步骤属于原来的 `Backend tests and Linux publish` 作业；前端独立运行。
任一步失败都会使后端检查失败并阻止后续后端发布。

1. `Check that model changes have a migration`：比较模型与快照，不连接数据库。
2. `Test fresh database and existing database migrations on SQL Server`：
   两个 EF 用例验证空库、旧库升级、产品数据保留、Note 保存和重复迁移。
3. `Generate reviewable migration SQL (no database access)`：生成幂等 SQL 和同次编译的迁移清单。
4. `Test deployment and read-only migration tooling without server access`：离线验证部署和只读预检查工具。
5. `Test generated SQL on disposable SQL Server databases`：
   三个用例执行将被上传的同一份 SQL，验证空库、旧库升级和重复执行。
   随后重新核对文件哈希，才上传 SQL 产物。

旧库场景从 `20260930150622_AddEmailAttachments` 升级，验证
`20261009164508_AddProductExampleAttr` 新增的 `Products.Note`。
这不是所有历史版本或未来迁移的完整覆盖；新增迁移应补充结构、数据和升级场景。
不要删除已执行过的历史迁移来修复 CI。

SQL 文件用例不通过 `MigrateAsync()` 完成被测试的升级；EF 仅用于准备旧结构和查询。
测试专用 `SqlBatchParser.cs` 识别普通 `GO` 和 `GO -- 注释`，
支持字符串、标识符、嵌套注释并保留内部换行；拒绝 GO 次数和 sqlcmd/shell 指令。
各段在同一个无连接池连接上执行，不加执行器包装。
客户端错误阻止后续段，但不保证同一段内后续语句不会运行，也不撤销此前已提交的迁移。
这是一次性 CI fixture，不是生产运行器或自动恢复机制。

## 隔离边界

- SQL 只在 GitHub-hosted Ubuntu 的一次性 SQL Server 2025 Express 容器执行；
  镜像固定 digest，公开密码仅用于临时容器。
- 连接固定为 `127.0.0.1:14333`，写入前核对服务器名 `project1-ci-sqlserver` 和主版本 17。
- 数据库自行命名为 `Project1CiMigration_<随机ID>_fresh/upgrade`，只清理自己创建的数据库。
- 必须有 GitHub 环境、明确启用参数和临时密码；GitHub 上缺配置会失败而非静默跳过。
- 不启动应用入口、不读取 appsettings/User Secrets/生产密码、不使用 Tailscale/SSH。
  SQL 和清单生成使用非 Development 环境及无效占位连接。

## 本地与 GitHub 验证

```powershell
python -m unittest discover -s scripts/deploy/tests -v
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj --configuration Release
```

本地执行隔离保护、清单一致性、分段器和 EF 脚本生成检查；五个 SQL 集成用例跳过。
不要伪造 GitHub 环境变量在电脑或生产服务器运行 SQL 测试。实际 SQL 结果必须查看新的 GitHub CI。
业务报告为 `backend.trx`，EF/纯代码检查为 `migrations.trx`，
生成 SQL 用例为 `migration-sql.trx`，均收入 `backend-test-results-<sha>`。

## 下载 SQL

合并后选择全部成功的 **Project1 CI / main / push**，核对完整 SHA、run ID 和 attempt。
某个产物存在不代表整次 CI 成功。下载 `project1-migrations-sql-<完整SHA>`，
根目录恰有 `migrations.sql`、`migration-manifest.json`、`build-info.txt` 和 `SHA256SUMS`。
分别提供幂等 SQL、有序迁移编号、构建来源及前三个文件的 SHA-256。
保留期 14 天；PR 产物仅供审阅，生产使用与应用版本匹配的成功 main/push 产物。
旧包缺清单或仍带自动执行批准说明时拒绝验证，使用本次清理合并后的新 CI 包。

哈希只验证完整性，不代替来源核对和 SQL 审阅。幂等 SQL 依赖历史与真实结构一致，
不自动修复漂移，不表示危险变更一定安全。
生产操作见[手动迁移检查单](MANUAL_MIGRATIONS.md)。
既有[只读预检查](MIGRATION_PRECHECK.md)和应用部署保留，均不执行生产迁移。

参考：[EF Core 正式迁移策略](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)、
[SQL Server GO](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/sql-server-utilities-statements-go?view=sql-server-ver17)。
