# 产品管理并入采购角色

`RetireCatalogManagerRole` 是数据 migration。应用后：

- `PROCUREMENT_OFFICER` 和 `ADMIN` 可以新增、修改、启用和停用产品。
- 产品菜单位于 Procurement settings；不再提供 `CATALOG_MANAGER` 角色选项或演示登录入口。
- 现有 Catalog Manager 账号获得 Procurement Officer 角色，其他角色、账号资料、部门、密码和业务历史保持不变。原停用账号仍停用。
- 同时拥有两个角色的账号只保留一条采购角色关联。旧角色的自定义 claims 合并到采购角色。
- 工作流模板和实例中的 Catalog Manager 角色分配转换为 Procurement Officer；相同动作中重复的角色分配合并。工作流执行历史不改写。
- 受影响账号的 SecurityStamp 会更新，旧登录会失效，需要重新登录。原本只有采购角色的账号无需转换。
- 新数据库缺少采购角色时，迁移会先创建它；demo seeding 不再创建 Catalog Manager 账号或角色。

代码已生成 migration，但未对本地或生产数据库执行。停止旧 API，在项目根目录更新数据库，然后启动新版 API 和前端：

```powershell
dotnet tool run dotnet-ef -- database update --project backend/Project1.Api --startup-project backend/Project1.Api --configuration Release
```

使用 `compose.local.yml` 时，使用同一数据库对应的 migrate 服务更新：

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml --profile tools run --rm --build migrate
docker compose --env-file .env.docker.local -f compose.local.yml up -d --build
```

无需再次运行 `migrations add`。已有库的 `catalog@demo.local` 账号仍保留原邮箱、姓名和密码，但其角色已转为采购员；新的演示环境只提供七个默认账号。

这次转换不能从转换后的数据推断各账号原来是否只属于 Catalog Manager，因此 Down 明确不支持自动反向转换。

验证范围包括：采购员产品创建和页面访问、仓库角色拒绝访问、旧角色不可再分配、账号转换与工作流继续执行、重复 demo seeding，以及迁移的重复执行。真实 SQL Server 集成用例按项目规则仅在 CI 中运行。
