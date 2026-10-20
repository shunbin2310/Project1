# API 启动自动迁移

开发者在本地修改 Entity，生成并审阅 migration，然后与代码一起提交：

~~~powershell
$env:Database__MigrateOnStartup = 'false'
dotnet tool run dotnet-ef -- migrations add DescribeYourChange `
    --project backend/Project1.Api --startup-project backend/Project1.Api
~~~

这里生成源码文件，不修改数据库。生产部署不运行 EF CLI，不在服务器生成 migration。

## 启动顺序

新 API 启动 → 校验 ExpectedDatabase → 查询 pending migrations → MigrateAsync →
成功后才初始化模板/用户、启动邮件 worker 和 HTTP。没有 pending 则跳过；迁移失败则拒绝启动。

默认关闭。生产 compose.ubuntu.yml 显式设置：

- Database__MigrateOnStartup=true
- Database__ExpectedDatabase=Project1Db
- Database__CommandTimeoutSeconds=180

本机 compose.host-sql.yml 也已显式开启，连接的是 Windows SQL Server，不是 Ubuntu。
EF 设计时/CI 模型检查设置 false，不应触发应用入口迁移。

## 权限和数据

Ubuntu 继续使用现有 Project1Db，保留用户、商品、订单和迁移历史。
专用应用账号需要 reader、writer 及数据库级 DDL 权限；不要使用 sa/db_owner。
db_ddladmin 能执行广泛的表结构操作，不仅仅是新增列，采用启动迁移意味着接受这项权限取舍。

管理员一次性准备权限，可审阅 scripts/docker/Prepare-UbuntuStartupMigrations.sql。
脚本不创建数据库、不删除业务表或数据，也不由 CI 自动执行。密码在服务器私下配置。

已执行的 migration 和 Snapshot 必须保留。审阅每次 Up/Down：
新增 nullable 列通常保留旧行，但删列、缩短长度、改类型和约束可能造成数据丢失或失败。
删除列后，Down 加回列不会恢复原值。危险迁移发布前仍需确认可用的备份和恢复方案。

## 失败处理

迁移开始前停止同库旧 API writer；CI 不做自动降级/数据库还原。
部署失败保留 /opt/project1-auto/needs-attention，由管理员检查实际 schema、日志和数据后再处理。
不要直接删标记重跑，也不要在数据库变化后盲目启动旧镜像。
成功后才为 API 启用 unless-stopped；失败时不会循环重跑迁移。

[自动部署流程](docker-cicd.md)。
