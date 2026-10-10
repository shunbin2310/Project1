# CI 数据库迁移测试

## 五种检查

- `Check that model changes have a migration`：比较 EF 模型和迁移快照，发现忘记生成迁移的情况。不连接数据库。
- `Test fresh database and existing database migrations on SQL Server`：在一次性 SQL Server 2025 Express 中实际执行迁移，发现 SQL 执行错误。
- `Test generated SQL on disposable SQL Server databases`：读取 CI 刚生成的同一份 `artifacts/migrations/migrations.sql`，直接执行 SQL 文件，测试下载脚本的路径。
- `Test restricted migration account on disposable SQL Server databases`：改用独立的受限 SQL 登录执行同一份脚本，检查本次 `Note` 升级需要的权限和失败行为。
- `Test coordinated execution and real backups on disposable SQL Server`：把受限 SQL 会话、数据库锁、真实临时库备份校验和持久批准账本串起来；见[执行流程 CI 验证](MIGRATION_EXECUTION.md)。

五步都属于原来的 `Backend tests and Linux publish` 作业。迁移测试失败会阻止后端发布；SQL 文件、受限账号或执行流程测试失败也不会上传 SQL。因此现有必需检查仍能阻止合并，不需要另加一个必需作业。

## 测试内容

1. 空库：从第一条迁移更新到代码中的最新迁移，检查迁移记录和 `Products.Note` 列。
2. 升级：先更新到 `20260930150622_AddEmailAttachments`，插入虚构产品，再更新到最新迁移。检查产品 ID、名称、价格等数据仍在，旧产品的 `Note` 为 NULL，新备注可以保存。
3. 再次运行迁移：不重复创建产品或迁移记录。

以上是原来两个 EF 迁移用例（第三项包含在升级用例中）。新增三个 SQL 文件用例：

1. 空库：直接执行生成的 SQL 文件，检查所有迁移记录、`Note` 列和空产品表。
2. 旧库：用 EF 准备 `Note` 之前的结构和虚构产品，再用 SQL 文件升级。检查原有产品字段保持不变、旧 `Note` 为 NULL、新备注能够保存。
3. 重复执行：用 SQL 文件建立结构，插入虚构产品并保存备注，再执行同一份 SQL。检查迁移记录、产品和备注都不变。

SQL 用例不会通过 `MigrateAsync()` 来完成被测试的升级；EF 只用于准备旧结构和查询结果。文件按 `GO` 分段，在同一个 SQL 连接上顺序执行，保留跨段事务和会话状态；客户端收到错误后不再发送后续分段。注意：这本身不保证发生错误的分段会立即停止，SQL Server 可能继续执行同一段中的后续语句，包括 COMMIT。受限账号的额外保护见下一节。分段器支持 EF 生成的普通 `GO` 行和 `GO -- 注释`，识别字符串、标识符和嵌套注释，不会误拆其中的 `GO`。它不是通用 sqlcmd 工具，不支持 `GO 2`、`:r`、`:connect` 或 shell 指令；碰到这些格式会让 CI 失败。

这是 `Note` 升级场景的回归测试，不代表覆盖所有历史版本的升级路径，也不能代替正式上线前的 SQL 审阅、备份和业务检查。以后有新的数据库变化，应补充对应的升级测试；不要为了让 CI 变绿而删除已执行的历史迁移。

## 受限迁移执行账号：只在 CI 练习

原来的 EF 和 SQL 文件测试使用容器的 `sa` 准备或执行测试；新步骤用 `sa` 准备旧结构、虚构产品和测试账号，
然后通过**独立 SQL 登录的真实连接**执行 SQL，不是用管理员连接 `EXECUTE AS` 来模拟。

每个账号名都是 `Project1CiExecutor_<32 位随机ID>`，密码随机生成，仅保存在测试进程内，不打印、不存入文件或 GitHub secrets。
只有确认固定 CI 服务器身份和本次随机数据库名后才能创建账号；创建失败关闭无连接池的管理员连接，使未提交的创建事务回滚。
正常测试结束后清理本次账号，再清理数据库；GitHub 最终销毁整个服务容器。

本次只测试已审阅的 `BeforeNote → AddProductExampleAttr` 升级，不测试受限账号创建完整空库。
账号不加入 `sysadmin`、`serveradmin`、`securityadmin`、`db_owner`、`db_ddladmin` 或 `db_securityadmin`。
只显式授予：

| 对象 | 权限 | 用途 |
| --- | --- | --- |
| 本次临时数据库 | CONNECT | 登录测试库 |
| `dbo.__EFMigrationsHistory` | SELECT、INSERT | 读取已完成迁移、记录新迁移 |
| `dbo.Products` | ALTER | 增加可空的 `Note` 列 |

受限账号通过执行保护层运行每个原始分段：设置 `XACT_ABORT ON`，把原始 SQL 放进本段的 `TRY...CATCH`，在同一连接上直接执行，不再套一层 `EXEC/sp_executesql`。这样事务可以在一段 BEGIN、后续段 COMMIT，不会因为进入/退出 EXEC 时事务计数不同触发 266。SQL 捕获错误时回滚仍打开的事务，再用 `THROW` 保留错误。保护代码只加在原始分段前后，分段内部的字节和上传文件不被修改；追加换行避免末尾 `--` 注释吞掉保护代码。原来的三个 SQL 文件测试仍直接执行原始分段，不使用这个保护层。

同层的语法、编译及部分名称解析错误不会被 SQL CATCH 捕获，因此 C# 收到错误后还会用独立的 10 秒超时尝试回滚打开的事务，并关闭无连接池的连接，不发送后续分段。清理成功时重新抛出原始错误；清理也失败时报告包含两项错误的 AggregateException，不把清理失败伪装成正常权限拒绝。266 不被当作权限拒绝或可忽略的成功。

这是 CI 测试执行策略，不是“下载 SQL 后随便执行就会自动回滚”的保证，也没有安装到 Ubuntu 或加入 CD。没有新增包住整个文件的外层事务，之前已提交的迁移不会被撤销。当前保护层验证本次生成文件及所列场景，不是通用 SQL 执行器：例如要求是批次第一条语句的 CREATE PROCEDURE 不能直接套这层 TRY。未来脚本必须重新审阅执行兼容性；不能为绕过失败忽略错误。

十个实际 SQL 测试检查：

1. 受限账号执行 CI 生成的原始 SQL 文件，升级成功、原产品不变；管理员保存备注后，同一账号再次执行不重复迁移、不清空备注。
2. 只授予历史表 SELECT 的只读账号无法升级，结构、历史和旧产品保持不变。
3. 有 Products ALTER、但缺少历史表 INSERT 的账号也必须失败，不留下已完成迁移记录或未提交的 Note 列。
4. 执行账号不能直接读取、插入、更新或删除产品数据，不能修改/删除历史记录，不能修改其他表、创建表、授予权限、加入 db_owner，不能进入另一个随机测试库。也检查自身没有服务器控制、修改登录、创建数据库或模拟其他登录的权限。
5. 人为在未提交的改表和历史 INSERT 之后抛出错误，保护层回滚事务且停止后续分段；随后用真实生成文件仍可正常升级。
6. 同一分段中，在改表和写历史之后触发转换错误；后面的 COMMIT、同段历史标记和下一段历史标记都不能生效，旧结构和数据保持不变。
7. 前一分段打开事务并修改 Products，后一分段尝试修改没有权限的 Departments；即使同段后面有 COMMIT，也必须回滚仍打开的事务。
8. 前一分段已成功提交 Note 迁移，后一分段失败；只撤销后一段未提交的历史标记，已提交的 Note 和正式迁移记录仍保留。
9. 正常事务跨三个分段：先 BEGIN 和改表，再写历史，最后 COMMIT；检查每段事务计数以及成功提交的数据，并确认重新执行生成文件仍幂等。
10. 前一分段有未提交的修改，后一分段出现语法错误；由客户端清理事务并关闭连接，保留原始 102 错误，不能运行后续段。

权限拒绝检查记录 SQL 错误编号及消息。`1088` 可能表示对象不存在或不可见：只在管理员连接先确认目标表存在、且错误消息包含该表名称时接受它。其他运行/语法错误不能当作权限拒绝。

**重要边界：ALTER 是表级权限，不是“只能新增 Note”的权限。** 它也允许对 Products 做其他结构修改，可能影响数据；
历史表 INSERT 也不是由数据库权限自动审核迁移编号。这里只验证本次 SQL 所需的权限，不是可直接复制到生产的通用权限方案。
新迁移涉及别的表、数据回填、非空列或新对象时，必须重新审阅 SQL、设计权限并新增测试，不能自动扩大授权。
测试失败后的回滚仅覆盖仍未提交的事务；**不会撤销脚本之前已提交的迁移**，不能替代备份或生产失败处理。

本阶段不修改 Ubuntu 账号、密码或 sudoers；现有 `project1_migrate` 保持只读，CD 没有增加执行迁移的选项。

## 隔离保护

- SQL Server 只启动在 GitHub 托管的临时测试机器上，不使用 Tailscale、SSH 或部署 secrets。
- 镜像固定 digest，使用 Express 版本；公开密码只用于一次性容器，不是正式密码。
- 测试连接只能指向 `127.0.0.1:14333`，并在任何数据库写入前检查服务器名 `project1-ci-sqlserver` 和 SQL Server 主版本 17。
- 不启动 API 的 `Program.cs`，不读取 appsettings 或 user secrets，不执行应用初始化或邮件发送。
- 数据库名由测试自行生成：`Project1CiMigration_<随机ID>_fresh/upgrade`。清理只删除本次测试创建的数据库。
- 新登录名也必须符合专用随机名称格式；拒绝 `sa`、`project1_migrate`、任意调用者账号名或生产数据库名。
- GitHub 会在作业结束时销毁服务容器。测试失败也不会影响 Ubuntu 的 `Project1Db`。

## 本地检查

需要 SQL Server 的二十一个用例只在 CI 的专门步骤执行。即使测试项目加入解决方案，普通电脑上的测试也不会连接 SQL Server。CI 的普通后端测试步骤明确运行原来的业务测试项目；EF 迁移与纯代码检查排除 SQL 文件、受限账号和执行流程用例，生成文件后再分别执行三个 SQL 文件用例、十个受限账号用例和六个执行流程用例。分别保存 `backend.trx`、`migrations.trx`、`migration-sql.trx`、`migration-executor.trx` 和 `migration-workflow.trx`，避免报告相互覆盖。

```powershell
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj --configuration Release
```

电脑上会执行纯代码的隔离保护、权限模板、执行命令构造、错误分类、SQL 分段器和当前 EF 脚本生成检查；二十一个需要 SQL Server 的测试会标记为跳过。在 GitHub 上这二十一个测试必须实际执行；缺少明确启用参数、密码、工作区或生成的文件会失败，不会静默跳过。

不要在 Ubuntu 上运行这个项目，也不要通过伪造 GitHub 环境变量让它连接电脑或正式数据库。真正的迁移执行结果请查看 PR 的 CI 日志和 `backend-test-results` 中的四个迁移 TRX 报告。

正式数据库迁移仍然由管理员备份、审阅 SQL 后手动执行。CD 现在另有可选的只读历史预检查，
但不会执行迁移；原正式部署 helper 不变。见 [只读迁移预检查](MIGRATION_PRECHECK.md)。

## 下载可审阅的迁移 SQL

后端业务测试、模型检查和 EF 迁移测试通过后，CI 执行 `Generate reviewable migration SQL (no database access)`。然后执行离线部署工具测试、三个 SQL 文件测试、十个受限账号测试和六个执行流程测试，核对 SQL、版本说明及迁移清单的校验值没有改变，最后通过 `Save reviewable migration SQL` 上传文件。生成、测试、校验或上传失败都会让后端作业失败。只有 SQL 集成测试步骤会执行 SQL，而且只在临时容器中；不使用正式连接字符串或部署 secrets。前端作业独立执行，因此下载正式上线用文件前仍要确认整次 CI 成功。

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

权限设计参考：[对象级 GRANT](https://learn.microsoft.com/en-us/sql/t-sql/statements/grant-object-permissions-transact-sql?view=sql-server-ver17)、[ALTER TABLE 所需权限](https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-table-transact-sql?view=sql-server-ver17#permissions)。

执行保护参考：[SET XACT_ABORT](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-xact-abort-transact-sql?view=sql-server-ver17)、[TRY...CATCH 的捕获边界](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/try-catch-transact-sql?view=sql-server-ver17)、[SQL Server 错误码 1088](https://learn.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors-1000-to-1999?view=sql-server-ver17)、[SQL Server 错误码 266](https://learn.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors-0-to-999?view=sql-server-ver17)。
