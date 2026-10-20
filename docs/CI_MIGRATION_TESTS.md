# CI 迁移测试

GitHub Actions 在一次性 SQL Server 2025 Express 容器中执行迁移测试，不连接 Ubuntu。

保留的检查：

- Entity 与 ModelSnapshot 是否一致。
- 从空数据库应用全部迁移。
- 从旧结构升级后，商品数据是否保留。
- API 启动自动应用 pending migrations；再次启动跳过；缺 DDL 权限时失败。
- EF 生成的幂等 SQL 在临时数据库中执行，并验证结构与数据。

临时连接固定为 127.0.0.1:14333，服务器身份 project1-ci-sqlserver，数据库名称随机生成。
必须明确启用 CI-only 参数；测试不读取应用 User Secrets 或生产连接配置。
普通本地 dotnet test 会跳过这些 SQL 集成用例；跳过不等于 SQL 验证通过。

生成的 SQL 和小型迁移编号清单仅用于 CI 测试，不上传到正式服务器，不作为 CD 发布包。
真实部署通过 API 镜像内的 MigrateAsync 应用迁移，见
[启动迁移说明](deployment/api-startup-migrations.md)。

本地检查：

~~~powershell
dotnet test tests/Project1.Api.Tests/Project1.Api.Tests.csproj
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj
python -m unittest discover -s scripts/deploy/tests -v
~~~
