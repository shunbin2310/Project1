# CI 数据库迁移测试

## 两种检查

- `Check that model changes have a migration`：比较 EF 模型和迁移快照，发现忘记生成迁移的情况。不连接数据库。
- `Test fresh database and existing database migrations on SQL Server`：在一次性 SQL Server 2025 Express 中实际执行迁移，发现 SQL 执行错误。

两步都属于原来的 `Backend tests and Linux publish` 作业。迁移测试失败会阻止后端发布，因此现有必需检查仍能阻止合并，不需要另加一个必需作业。

## 测试内容

1. 空库：从第一条迁移更新到代码中的最新迁移，检查迁移记录和 `Products.Note` 列。
2. 升级：先更新到 `20260930150622_AddEmailAttachments`，插入虚构产品，再更新到最新迁移。检查产品 ID、名称、价格等数据仍在，旧产品的 `Note` 为 NULL，新备注可以保存。
3. 再次运行迁移：不重复创建产品或迁移记录。

这是 `Note` 升级场景的回归测试，不代表覆盖所有历史版本的升级路径，也不能代替正式上线前的 SQL 审阅、备份和业务检查。以后有新的数据库变化，应补充对应的升级测试；不要为了让 CI 变绿而删除已执行的历史迁移。

## 隔离保护

- SQL Server 只启动在 GitHub 托管的临时测试机器上，不使用 Tailscale、SSH 或部署 secrets。
- 镜像固定 digest，使用 Express 版本；公开密码只用于一次性容器，不是正式密码。
- 测试连接只能指向 `127.0.0.1:14333`，并在任何数据库写入前检查服务器名 `project1-ci-sqlserver` 和 SQL Server 主版本 17。
- 不启动 API 的 `Program.cs`，不读取 appsettings 或 user secrets，不执行应用初始化或邮件发送。
- 数据库名由测试自行生成：`Project1CiMigration_<随机ID>_fresh/upgrade`。清理只删除本次测试创建的数据库。
- GitHub 会在作业结束时销毁服务容器。测试失败也不会影响 Ubuntu 的 `Project1Db`。

## 本地检查

需要 SQL Server 的两个用例只在 CI 的专门步骤执行。即使测试项目加入解决方案，普通电脑上的测试也不会连接 SQL Server。CI 的普通后端测试步骤明确运行原来的业务测试项目，新迁移测试项目在单独步骤执行，分别保存 `backend.trx` 和 `migrations.trx`，避免两个项目的报告相互覆盖。

```powershell
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj --configuration Release
```

电脑上会执行纯代码的隔离保护测试；两个需要 SQL Server 的测试会标记为跳过。在 GitHub 上这两个测试必须实际执行；缺少明确启用参数或密码会失败，不会静默跳过。

不要在 Ubuntu 上运行这个项目，也不要通过伪造 GitHub 环境变量让它连接电脑或正式数据库。真正的迁移执行结果请查看 PR 的 CI 日志和 `backend-test-results` 中的 `migrations.trx`。

正式数据库迁移仍然由管理员备份、审阅 SQL 后手动执行；这次没有改 CD 或正式部署 helper。

## 下载可审阅的迁移 SQL

后端业务测试、模型检查和 SQL Server 迁移测试通过后，CI 执行 `Generate reviewable migration SQL (no database access)`，再通过 `Save reviewable migration SQL` 上传文件。生成或上传失败会让后端作业失败；这两步不会执行 SQL，也不使用正式连接字符串或部署 secrets。前端作业独立执行，因此下载正式上线用文件前仍要确认整次 CI 成功。

1. 合并 PR 后，打开 GitHub → Actions → **Project1 CI**。
2. 选择对应代码版本、事件为 **push**、分支为 **main** 的成功运行。不要用运行序号代替 run ID。
3. 在运行的 Summary 下方找到 **Artifacts**，下载 `project1-migrations-sql-<完整代码SHA>`。
4. 解压后查看：
   - `migrations.sql`：从第一个迁移到该版本最新迁移的 SQL Server 脚本。
   - `build-info.txt`：代码 SHA、实际检出的提交、CI run ID、重跑次数、事件、分支引用和运行链接。
   - `SHA256SUMS`：SQL 和版本说明文件的 SHA-256。下载后可用 `Get-FileHash` 或 `sha256sum -c SHA256SUMS` 核对文件；校验值用于核对文件完整性，不代表 SQL 一定安全。

PR 中也会生成文件供审阅，但其 SHA 通常是 GitHub 测试用的合并提交，不是功能分支的提交。正式上线时应使用与应用部署版本匹配的成功 **main push** 运行，并核对 `build-info.txt`。文件保留 14 天；如需长期保存，请另行归档。

生成命令是 `dotnet ef migrations script 0 --idempotent`。`--idempotent` 的意思是：SQL 会查看 `__EFMigrationsHistory`，跳过已记录为完成的迁移。因此文件包含完整迁移历史，并不只是这次新增的 `Note`。生成时明确使用非 Development 环境和无效占位连接，不连接电脑或正式数据库。

注意：

- 这一步只生成 SQL，不会创建新的 C# migration；修改实体后仍需手动运行 `migrations add` 并提交迁移文件。
- 现有 SQL Server 集成测试验证的是 EF 执行迁移的路径，不是下载脚本的执行路径；生成成功不等于脚本已经执行测试。
- `--idempotent` 依赖迁移记录与实际结构一致，不会自动修复结构漂移，也不保证删除列、转换数据等操作安全。脚本中可能包含历史删除操作，必须审阅目标数据库尚未执行的部分，并先在测试库试运行。
- SQL 脚本没有 EF 的迁移锁；正式执行时应协调维护窗口，避免多人同时迁移。
- 正式使用前仍需要核对数据库与迁移记录、备份、审阅、测试并确认应用兼容性。执行后查询迁移记录和业务数据，再按原流程部署应用。
- 不要因为多了下载文件就再次执行已经完成的 `Note` 迁移，也不要把这个文件交给现有 CD 自动执行。

参考：[GitHub 服务容器](https://docs.github.com/en/actions/tutorials/use-containerized-services/use-docker-service-containers)、[微软 SQL Server 容器说明](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/quickstart-install-docker?view=sql-server-linux-ver17)、[EF Core 迁移脚本与正式迁移说明](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)。
