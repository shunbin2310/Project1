# 数据库迁移：只读预检查

采用 **CI 验证 + 手动迁移**。这里仅比较迁移历史，**不执行迁移**；应用部署工具没有执行 SQL 的权限。

生产迁移由管理员按[手动迁移检查单](MANUAL_MIGRATIONS.md)另行审阅、备份和执行。
工作流不再提供迁移执行选项、批准登记或账本；现有只读工具及生产权限保持不变。

## 它做什么

1. 你指定本仓库一次成功的 `Project1 CI / main / push` 运行 ID 和完整提交 SHA。
2. 无服务器访问的 validate 作业校验 API、前端和迁移包。迁移包必须恰好包含四个根目录文件：
   `migrations.sql`、`build-info.txt`、`migration-manifest.json`、`SHA256SUMS`。
3. 核对 GitHub ZIP digest、包内三个文件的哈希、版本说明中的仓库/SHA/运行 ID/attempt/event/ref，
   以及 SQL 中的历史记录 INSERT 编号与迁移清单是否一致。只读取 ZIP，不解压执行其中的代码或 SQL。
4. 明确勾选只读检查后，第二个作业在全新 runner 上再次验证全部包和运行次数/哈希，之后才加入 Tailscale、连接 SSH。
5. 服务器只运行管理员安装的固定查询工具。GitHub 不接收数据库密码，SQL 文件也不会上传到服务器。
6. 对比数据库历史和该版本的有序迁移清单，在 Actions Summary 中显示已完成及待执行迁移。

CI 从同一次构建的 EF 程序集，用 `migrations list --no-connect --json --prefix-output` 生成清单。
非 Development 环境及占位配置与 SQL 生成步骤一致，不连接本地或正式数据库、不读取本地 user secrets。
四个生成 SQL 集成用例执行前，也核对清单与该次编译的迁移一致；全部通过并重新核对哈希后才上传。
**旧 CI 包缺少清单或仍带旧的自动执行批准说明时会被拒绝；请选清理变更合并后产生的新 main/push CI。**

## 三种手动运行方式

Actions → **Project1 manual deployment** → Run workflow → Branch: `main`：

| deploy | check_migrations | 行为 |
| --- | --- | --- |
| 不勾选 | 不勾选 | 原来的包验证模式；不连接服务器 |
| 勾选 | 不勾选 | 原来的 API/前端部署；不执行迁移 |
| 不勾选 | 勾选 | 新的只读迁移检查；不上传、不停服务、不执行迁移 |
| 勾选 | 勾选 | 立即拒绝，不能同时操作 |

所有方式使用同一个工作流并发组 `project1-manual-deployment`，不自动取消正在执行的运行。
这只能协调此 GitHub 工作流，不能锁住管理员从其他终端执行的迁移。预检查没有数据库迁移锁。
保持原工作流路径、main/手动事件和现有 Tailscale OIDC 配置，不增加 `environment:`。
只读作业复用已有四个 repository secrets，不新增数据库密码 secret。

## 服务器工具的边界

新增的三个文件由管理员审阅后手动安装，CI/CD 不安装、不更新自身授权：

| 仓库源文件 | 服务器目标 | 所有者 / 权限 |
| --- | --- | --- |
| `scripts/deploy/project1_migration_status.py` | `/usr/local/libexec/project1-migration-status.py` | root:root / 0644 |
| `scripts/deploy/project1-migration-status` | `/usr/local/sbin/project1-migration-status` | root:root / 0755 |
| `scripts/deploy/project1-migration-status.sudoers` | `/etc/sudoers.d/project1-migration-status` | root:root / 0440 |

前提：`/etc/project1-migration` 已是 root 管理的真实目录、0700；`password` 是 root 所有的普通单硬链接文件、0600。
密码文件为之前生成的 `P1!` 加 64 位小写十六进制随机字符及换行；不要发送内容、截图或提交到 Git。
SQL 登录/数据库用户 `project1_migrate` 已存在，只显式授予 CONNECT 和 `dbo.__EFMigrationsHistory` 的 SELECT。

工具不接受参数，尤其不接受 SQL、路径、连接字符串或环境变量指定目标。
固定连接 `tcp:127.0.0.1,1433` / `Project1Db` / `project1_migrate`，核对服务器名、账号和数据库状态。
只检查自身有效权限和查询迁移记录；发现意外的广泛/写入权限时停止，不自动撤销或追加权限。
sqlcmd 是 root 管理的固定二进制；root 组可写的已有文件允许，其他身份可写或任一软链接路径拒绝。
密码仅通过短时 sqlcmd 子进程环境传递，不放在 argv，不继承 SQLCMDINI/调用者环境，不接受 stdin，
不打印原始数据库/SSH 错误。这个 root 入口不是通用 SQL 运行器。
使用本机回环地址及 `-C` 信任现有 SQL Server 证书；SSH 对主机公钥的固定校验仍保留。

## 管理员安装顺序

这是安装检查单，不是让 GitHub 执行的脚本。先完成本地测试、PR 审阅和新的 main CI。
每一步出错立即停止，不覆盖已有目标，也不要修改原 `project1-deploy` 或其 sudoers。

1. 从已审阅的仓库版本计算以上三个源文件的 SHA-256，上传到 `shunbin` 私有的新暂存目录。
   在服务器核对三个文件哈希，必须与电脑计算结果一致。
2. 确认三个目标既不存在也不是软链接；确认 `/usr/local/libexec` 和 `/usr/local/sbin` 是 root 管理的真实目录，
   不允许部署用户写入。确认密码目录/文件权限，但不要输出密码内容。
3. 用 `sh -n <暂存目录>/project1-migration-status` 检查入口。
   用 Python 的 `ast.parse` 检查 Python 文件语法（不要通过执行工具来代替语法检查）。
4. 管理员使用 `install -o root -g root -m 0644` 安装 Python 实现、`-m 0755` 安装 shell 入口。
   两个文件安装成功后，先以管理员运行下面的固定查询并检查 JSON 结果：

   ```bash
   sudo /usr/local/sbin/project1-migration-status
   ```

   应返回 `schema: 1`、`homelab-server`、`Project1Db`、`project1_migrate`、UTC 时间和有序迁移编号。
   如果失败，停止检查 root 文件元数据、密码文件元数据和账号权限；不输出密码文件、不扩大权限以绕过检查。
5. 用 `/usr/sbin/visudo -c` 检查当前完整配置，再用 `visudo -c -f <暂存sudoers文件>` 检查模板。
   管理员用 `install -o root -g root -m 0440` 安装单独 sudoers 文件，立即再次运行 `visudo -c`。
   若完整校验失败，把本次新增的 sudoers 文件移出 `/etc/sudoers.d` 到 root 私有保留目录，重新校验并停止；
   不改动其他授权文件。具体路径和命令在首次安装时逐步确认。
6. 授权成功后测试：

   ```bash
   sudo -l -U project1_deploy
   sudo -u project1_deploy -- sudo -n /usr/local/sbin/project1-migration-status
   ```

   新增授权应只有无参数的 status 工具，原部署授权保留。sudoers 的 `""` 明确限制无参数调用。
   带参数的调用应该被拒绝，例如下面命令的非零退出是预期结果：

   ```bash
   sudo -u project1_deploy -- sudo -n /usr/local/sbin/project1-migration-status --sql anything.sql
   ```

7. 以上全部通过后，才在 GitHub 勾选 `check_migrations`，**不勾选 deploy**，选择新的成功 main/push CI。

本功能没有运行生产迁移的选项。需要变更数据库时，使用[手动迁移检查单](MANUAL_MIGRATIONS.md)，单独确认权限、备份、维护窗口和失败处理。

## 怎样看报告

- `Pending migration count: 0`：这次查询时，迁移历史与所选版本一致。
- 大于 0：列出的迁移在历史表中尚未完成。这里只报告，不执行，不自动批准应用部署。
- 数据库历史不是所选版本清单的完整前缀：拒绝。例如选了旧版本、历史缺项、未知迁移、重复编号。
- SQL 包校验失败：不会连接服务器；换版本前先查明原因，不放宽校验。
- SSH/helper/权限失败：停止，由管理员检查工具是否安装、固定主机公钥、Tailscale 和账号配置。

报告是某个时间点的历史记录比较，不检查所有真实表/列是否被手工修改，也不判断 DROP 等操作是否安全。
数据库仍可能被应用或管理员修改；业务数据和数据库密码不会进入报告。
上线前仍需审阅 SQL、验证目标结构、准备新备份及恢复方案，并明确授权。代码回滚不会回滚数据库。

## 开发者验证（不连接生产）

```powershell
python -m unittest discover -s scripts/deploy/tests -v
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj --configuration Release
```

Python 测试只使用虚构 ZIP、GitHub 响应和模拟 SSH/sqlcmd。Linux 专用文件测试只使用临时目录；
Windows 跳过这些用例。普通电脑上的六个 SQL 集成测试跳过；实际 SQL Server 验证必须等待 GitHub 临时容器测试。生产预检查始终不执行迁移。
工作流应使用 actionlint 校验；本地 Windows 无法代替 Ubuntu 上的 visudo 或真实 sqlcmd/只读账号验收。

参考：[EF CLI 的迁移清单和 --no-connect](https://learn.microsoft.com/en-us/ef/core/cli/dotnet#dotnet-ef-migrations-list)、
[GitHub artifact API](https://docs.github.com/en/rest/actions/artifacts)、
[sqlcmd 参数和密码输入](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility?view=sql-server-ver17)、
[sudoers 参数限制](https://manpages.ubuntu.com/manpages/noble/man5/sudoers.5.html)。
