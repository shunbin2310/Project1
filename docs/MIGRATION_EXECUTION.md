# 迁移执行流程：CI 验证阶段

本阶段把批准账本、包校验、备份证据、数据库锁、受限 SQL 执行和结果记录串起来。
**没有生产命令入口、安装脚本、生产执行账号、sudoers 授权或 CD 执行选项。**
不要把这些库、CI 测试宿主或原离线校验器直接加入生产 sudoers。

## 实现范围

- `scripts/deploy/migration_execution.py`：只接受管理员控制的已登记批准编号，不初始化账本、不登记批准，
  不接收调用者 SQL、连接参数或替换批准 JSON。它是库，不是生产命令。
- `scripts/deploy/dotnet/Project1.MigrationExecutor`：实际 SQL 会话实现，无可执行程序、配置读取或生产连接工厂。
  唯一连接工厂要求 GitHub CI 明确启用，只连接 `tcp:127.0.0.1,14333`，
  固定服务器 `project1-ci-sqlserver`、SQL Server 17 和随机命名的 upgrade 数据库/执行账号。
- `migration_target.py`：可信宿主指定目标。既有离线/只读 CLI 仍固定生产目标，没有新 target 参数。
  CI 批准使用真实 CI 身份，不能伪装成 `homelab-server/Project1Db`；生产默认校验拒绝 CI 记录/账本。

当前执行配置**只支持待执行范围恰好为 `20261009164508_AddProductExampleAttr`**。
这是已审阅、已验证权限的可空 `Products.Note` 升级，不是任意迁移或全库初始化功能。
新迁移需要单独定义结构检查、权限、备份/恢复要求和兼容性测试，不能自动扩大权限。

## 一次执行的顺序

1. 从可信持久账本读取原批准；拒绝已领取批准及任何未解决的 claimed/failed/interrupted 任务。
2. 重新校验迁移 ZIP/清单，复制 SQL 为执行进程自己的字节，核对 SQL SHA-256。
3. 通过可信宿主的在线来源检查接口核对选定 CI 版本/attempt/产物。
4. 独立受限 SQL 会话取得 `Project1.MigrationExecution` 的 Session/Exclusive 数据库应用锁，
   不等待冲突、不开启重连、不重试迁移，连接不使用连接池。
5. 持锁核对真实身份、历史前缀、限定 Note 结构，以及独立备份校验证据。
6. 再查历史，持久领取批准；账本文件和目录刷新成功之前不得开始 SQL。
7. 再检查来源、批准有效期、实时历史与结构；执行先前保存的同一 SQL 字节。
8. 逐 GO 分段执行，每段有内联 `XACT_ABORT/TRY/CATCH`；收到错误立即停止。
   不使用 EXEC 包装原分段，以保留跨 GO 事务；编译错误另由客户端回滚打开的事务。
9. 持锁确认完整历史、Note 的类型/可空性及最终无打开事务，持久记录 succeeded 后关闭会话。

库接口中的包来源、在线检查、SQL 会话及备份验证器必须来自可信宿主代码。
**不能由部署用户传入 callbacks、JSON 断言或任意模块来替代这些边界。**
SQL 层会再次核对字节哈希、身份、锁与配置所需权限；发现数据库/服务器广泛权限或业务 DML 权限时拒绝。
这不是对所有 SQL 权限的穷举审计，生产仍需要独立审阅账号、角色、public 授权与对象所有权。
`ALTER Products` 不是“只允许增加 Note”的数据库权限，安全性依赖审批的精确 SQL 及可信固定入口。

## 备份证据与 CI 的真实性边界

执行流程要求独立证据与批准的备份 SHA-256、完成时间、服务器和数据库相同，并且校验时间新鲜。
还要求 CHECKSUM、COPY_ONLY 和 VERIFYONLY 确认；仅有批准 JSON 中的 `verified: true` 会被拒绝。

两条端到端 CI 用例用**单独的管理员 fixture**在服务容器中实际执行 COPY_ONLY/CHECKSUM BACKUP，
核对 RESTORE HEADERONLY 中的数据库、服务器及标志，实际执行 CHECKSUM/STOP_ON_ERROR VERIFYONLY，
并通过本作业服务容器的 `sha256sum` 在验证前后核对备份文件。
只操作本次随机数据库对应的固定容器路径，备份随整个临时容器销毁；不接触 Ubuntu 正式备份。
受限执行账号不获得备份或恢复权限，密码不传给 Python 子进程。

CI 的批准是测试批准，ZIP 来源元数据是围绕本次实际生成 SQL 构造的 fixture。
**在线来源检查接口在这些测试中是模拟的**，不能据此宣称已验证生产 GitHub 产物或真实管理员批准。
生产的在线来源适配器、备份验证器、批准注册/恢复入口及权限安装尚未实现。
现有生产工作流仍只执行原应用部署或只读历史预检查。

VERIFYONLY 检查不等于一次完整恢复演练，也不保证全部恢复条件。
正式启用前仍要验证恢复路径、应用兼容性、维护窗口和备份后的业务写入处理。

## 失败和中断

- SQL 前拒绝通常保留 approved；若持久领取已发生，后续失败记录 failed，中断记录 interrupted。
- 失败、中断和未解决领取均阻止其他批准；更换批准 ID 不会绕过阻塞。
- 领取写盘失败不开始 SQL；完成写盘失败保留 claimed，不自动重试已可能提交的 SQL。
- 会话清理回滚仍打开的事务，并通过关闭无连接池会话释放锁。
- 不撤销此前已提交的分段，不自动恢复数据库、不重新发放批准、不清理账本。
- 进程崩溃可能无法写入 interrupted；持久 claimed 本身继续阻止执行。
- 不保证 SQL 恰好执行一次。管理员必须结合真实历史、结构、日志和备份判断恢复方案。
- 会话应用锁只协调遵守同一锁协议的执行器，不锁住任意管理员 SQL 或应用写入。
  不使用这个锁取代维护窗口，也不声称与 EF 自身的迁移锁互通。

## 验证

普通电脑运行：

```powershell
python -m unittest discover -s scripts/deploy/tests -v
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj --configuration Release
```

可移植用例检查顺序、字节绑定、备份/来源/过期拒绝、失败状态、防重放和脱敏。
Linux 用例在测试临时目录运行真实持久化；仅在测试适配所有者/UID，不使用生产目录。
Windows 跳过这些 Linux 用例及全部真实 SQL 集成用例，不伪造 CI 环境。

GitHub 新步骤 `Test coordinated execution and real backups on disposable SQL Server` 运行六个 SQL 用例：

1. 真实备份验证、实际生成 SQL、持久账本成功完成；重新打开账本不能再次执行。
2. 带单独绑定批准的 CI 故障脚本：最后事务提交前失败；DDL/历史回滚，旧产品保留，失败阻止重试。
3. 第二个独立会话不能获取已持有的锁；第一个关闭后可以获取。
4. 错误字节哈希及不符合历史的 Note 结构被拒绝。
5. 跨 GO 的运行错误和编译错误均回滚未提交 DDL/历史，不执行后续 COMMIT，原会话不能重试。
6. 正常跨 GO 事务可提交，执行后仍持有锁。

新增报告 `migration-workflow.trx`；保留原始测试及文件校验，任一失败阻止 SQL 上传和后端发布。
这六个用例的真实 SQL/备份结果必须以 GitHub 临时容器 CI 为准，本地编译通过不能替代。

参考：[SQL Server sp_getapplock](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql?view=sql-server-ver17)、
[RESTORE VERIFYONLY 的范围](https://learn.microsoft.com/en-us/sql/t-sql/statements/restore-statements-verifyonly-transact-sql?view=sql-server-ver17)。
