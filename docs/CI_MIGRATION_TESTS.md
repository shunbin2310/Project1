# CI 数据库迁移测试

## 三种检查

- `Check that model changes have a migration`：比较 EF 模型和迁移快照，发现忘记生成迁移的情况。不连接数据库。
- `Test fresh database and existing database migrations on SQL Server`：在一次性 SQL Server 2025 Express 中实际执行迁移，发现 SQL 执行错误。
- `Test generated SQL on disposable SQL Server databases`：读取 CI 刚生成的同一份 `artifacts/migrations/migrations.sql`，直接执行 SQL 文件，测试下载脚本的路径。

三步都属于原来的 `Backend tests and Linux publish` 作业。迁移测试失败会阻止后端发布；SQL 文件测试失败也不会上传 SQL。因此现有必需检查仍能阻止合并，不需要另加一个必需作业。

## 测试内容

1. 空库：从第一条迁移更新到代码中的最新迁移，检查迁移记录和 `Products.Note` 列。
2. 升级：先更新到 `20260930150622_AddEmailAttachments`，插入虚构产品，再更新到最新迁移。检查产品 ID、名称、价格等数据仍在，旧产品的 `Note` 为 NULL，新备注可以保存。
3. 再次运行迁移：不重复创建产品或迁移记录。

以上是原来两个 EF 迁移用例（第三项包含在升级用例中）。新增三个 SQL 文件用例：

1. 空库：直接执行生成的 SQL 文件，检查所有迁移记录、`Note` 列和空产品表。
2. 旧库：用 EF 准备 `Note` 之前的结构和虚构产品，再用 SQL 文件升级。检查原有产品字段保持不变、旧 `Note` 为 NULL、新备注能够保存。
3. 重复执行：用 SQL 文件建立结构，插入虚构产品并保存备注，再执行同一份 SQL。检查迁移记录、产品和备注都不变。

SQL 用例不会通过 `MigrateAsync()` 来完成被测试的升级；EF 只用于准备旧结构和查询结果。文件按 `GO` 分段，在同一个 SQL 连接上顺序执行，保留跨段事务和会话状态；遇到第一条错误立即停止。分段器支持 EF 生成的普通 `GO` 行和 `GO -- 注释`，识别字符串、标识符和嵌套注释，不会误拆其中的 `GO`。它不是通用 sqlcmd 工具，不支持 `GO 2`、`:r`、`:connect` 或 shell 指令；碰到这些格式会让 CI 失败。

这是 `Note` 升级场景的回归测试，不代表覆盖所有历史版本的升级路径，也不能代替正式上线前的 SQL 审阅、备份和业务检查。以后有新的数据库变化，应补充对应的升级测试；不要为了让 CI 变绿而删除已执行的历史迁移。

## 隔离保护

- SQL Server 只启动在 GitHub 托管的临时测试机器上，不使用 Tailscale、SSH 或部署 secrets。
- 镜像固定 digest，使用 Express 版本；公开密码只用于一次性容器，不是正式密码。
- 测试连接只能指向 `127.0.0.1:14333`，并在任何数据库写入前检查服务器名 `project1-ci-sqlserver` 和 SQL Server 主版本 17。
- 不启动 API 的 `Program.cs`，不读取 appsettings 或 user secrets，不执行应用初始化或邮件发送。
- 数据库名由测试自行生成：`Project1CiMigration_<随机ID>_fresh/upgrade`。清理只删除本次测试创建的数据库。
- GitHub 会在作业结束时销毁服务容器。测试失败也不会影响 Ubuntu 的 `Project1Db`。

## 本地检查

需要 SQL Server 的五个用例只在 CI 的专门步骤执行。即使测试项目加入解决方案，普通电脑上的测试也不会连接 SQL Server。CI 的普通后端测试步骤明确运行原来的业务测试项目；EF 迁移与纯代码检查排除 SQL 文件用例，生成文件后再单独执行三个 SQL 文件用例。分别保存 `backend.trx`、`migrations.trx` 和 `migration-sql.trx`，避免报告相互覆盖。

```powershell
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj --configuration Release
```

电脑上会执行纯代码的隔离保护、SQL 分段器和当前 EF 脚本生成检查；五个需要 SQL Server 的测试会标记为跳过。在 GitHub 上这五个测试必须实际执行；缺少明确启用参数、密码、工作区或生成的文件会失败，不会静默跳过。

不要在 Ubuntu 上运行这个项目，也不要通过伪造 GitHub 环境变量让它连接电脑或正式数据库。真正的迁移执行结果请查看 PR 的 CI 日志和 `backend-test-results` 中的两个迁移 TRX 报告。

正式数据库迁移仍然由管理员备份、审阅 SQL 后手动执行。CD 现在另有可选的只读历史预检查，
但不会执行迁移；原正式部署 helper 不变。见 [只读迁移预检查](MIGRATION_PRECHECK.md)。

## 下载可审阅的迁移 SQL

后端业务测试、模型检查和 EF 迁移测试通过后，CI 执行 `Generate reviewable migration SQL (no database access)`。然后执行离线部署工具测试和三个 SQL 文件测试，核对 SQL、版本说明及迁移清单的校验值没有改变，最后通过 `Save reviewable migration SQL` 上传文件。生成、测试、校验或上传失败都会让后端作业失败。只有 SQL 集成测试步骤会执行 SQL，而且只在临时容器中；不使用正式连接字符串或部署 secrets。前端作业独立执行，因此下载正式上线用文件前仍要确认整次 CI 成功。

1. 合并 PR 后，打开 GitHub → Actions → **Project1 CI**。
2. 选择对应代码版本、事件为 **push**、分支为 **main** 的成功运行。不要用运行序号代替 run ID。
3. 在运行的 Summary 下方找到 **Artifacts**，下载 `project1-migrations-sql-<完整代码SHA>`。
4. 解压后查看：
   - `migrations.sql`：从第一个迁移到该版本最新迁移的 SQL Server 脚本。
   - `build-info.txt`：代码 SHA、实际检出的提交、CI run ID、重跑次数、事件、分支引用和运行链接。
   - `SHA256SUMS`：SQL、版本说明和迁移清单文件的 SHA-256。下载后可用 `Get-FileHash` 或 `sha256sum -c SHA256SUMS` 核对文件；校验值用于核对文件完整性，不代表 SQL 一定安全。
   - `migration-manifest.json`：同一次编译的有序迁移编号清单，也在 `SHA256SUMS` 中校验。
     CI 使用 `migrations list --no-connect` 生成，核对 SQL 历史 INSERT，并在 SQL 集成测试前核对编译的 EF 迁移。

PR 中也会生成文件供审阅，但其 SHA 通常是 GitHub 测试用的合并提交，不是功能分支的提交。正式上线时应使用与应用部署版本匹配的成功 **main push** 运行，并核对 `build-info.txt`。文件保留 14 天；如需长期保存，请另行归档。

生成命令是 `dotnet ef migrations script 0 --idempotent`。`--idempotent` 的意思是：SQL 会查看 `__EFMigrationsHistory`，跳过已记录为完成的迁移。因此文件包含完整迁移历史，并不只是这次新增的 `Note`。生成时明确使用非 Development 环境和无效占位连接，不连接电脑或正式数据库。

注意：

- SQL 生成步骤不会创建新的 C# migration；修改实体后仍需手动运行 `migrations add` 并提交迁移文件。
- CI 现在同时测试 EF 迁移与生成 SQL 文件两条路径，且测试与上传使用同一份 SQL。`build-info.txt` 会说明只在一次性测试库执行过，CD 从不执行；测试通过仍不代表覆盖所有正式数据库状态。
- `--idempotent` 依赖迁移记录与实际结构一致，不会自动修复结构漂移，也不保证删除列、转换数据等操作安全。脚本中可能包含历史删除操作，必须审阅目标数据库尚未执行的部分，并先在测试库试运行。
- SQL 脚本没有 EF 的迁移锁；正式执行时应协调维护窗口，避免多人同时迁移。
- 正式使用前仍需要核对数据库与迁移记录、备份、审阅、测试并确认应用兼容性。执行后查询迁移记录和业务数据，再按原流程部署应用。
- 不要因为多了下载文件就再次执行已经完成的 `Note` 迁移，也不要把这个文件交给现有 CD 自动执行。

参考：[GitHub 服务容器](https://docs.github.com/en/actions/tutorials/use-containerized-services/use-docker-service-containers)、[微软 SQL Server 容器说明](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/quickstart-install-docker?view=sql-server-linux-ver17)、[EF Core 迁移脚本与正式迁移说明](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)、[SQL Server 的 GO 分段说明](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/sql-server-utilities-statements-go?view=sql-server-ver17)。
