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

参考：[GitHub 服务容器](https://docs.github.com/en/actions/tutorials/use-containerized-services/use-docker-service-containers)、[微软 SQL Server 容器说明](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/quickstart-install-docker?view=sql-server-linux-ver17)。
