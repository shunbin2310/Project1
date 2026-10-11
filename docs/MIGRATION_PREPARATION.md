# 自动准备，手动迁移

你手动停止 API → GitHub 上传 SQL 并备份 → 你审核、保存异机备份、手动执行 SQL → 验证 → 启动兼容 API → 部署应用。

这不是自动执行 migration。`prepare_migrations` 默认关闭，只有你手动触发时才访问服务器。
原来的执行器、宿主、批准账本不会恢复；现有 CI 验证、应用部署和只读检查保留。
不要为练习重复执行已完成的 migration，也不要删除旧 migration/history。

## 1. 一次性的 Ubuntu 安装（管理员手动做）

先提交代码、确认新 PR CI 成功、合并，并确认对应 main/push CI 成功。
安装前审阅这次的服务器 Python 工具、SQL 安装模板和独立 sudoers 模板。
**GitHub 不会安装这些工具、创建账号、修改 sudoers，也不接收数据库管理密码。**

前提：既有 `project1_deploy`、root-managed 应用部署工具及 `/var/lib/project1-deploy`，
现有只读工具 `/usr/local/libexec/project1-migration-status.py` 和它的凭据已经正常工作。
既有 `/home/project1_deploy/uploads` 可由部署账号创建子目录；
`/var/opt/mssql/backup` 的最终目录必须 root-owned 且不允许组/其他用户写入（当前服务器为 root:root、0755）。
本机 SQL 工具为 `/opt/mssql-tools18/bin/sqlcmd`；固定目标 `homelab-server / Project1Db`。
安装只创建备份支持对象，不修改业务表、不备份、不执行 migration、不操作 API 服务。

### Ubuntu：创建上传目录

```bash
Stage=$(mktemp -d "$HOME/project1-prepare-install-upload.XXXXXXXX")
chmod 0700 "$Stage"
printf '%s\n' "$Stage"
```

保留这个 SSH 窗口；在电脑另开 PowerShell。

### 电脑 PowerShell：上传审阅过的工具

在仓库根目录，用刚才打印的真实目录替换 `$RemoteStage`。只使用审核过的版本。

```powershell
$RemoteStage = '/home/shunbin/这里替换为真实上传目录'
$Files = @(
    'project1_migration_prepare.py',
    'project1_deploy.py',
    'project1_migration_status.py',
    'migration_package.py',
    'verify_ci_artifacts.py',
    'install_migration_prepare.py',
    'install_migration_prepare.sql',
    'project1-migration-prepare',
    'project1-migration-prepare.sudoers'
)
foreach ($Name in $Files) {
    scp -o StrictHostKeyChecking=yes (Join-Path 'scripts/deploy' $Name) "shunbin@192.168.1.158:$RemoteStage/"
    if ($LASTEXITCODE -ne 0) { throw '上传失败，先停止。' }
}
```

### Ubuntu：复制到 root 私有目录，再安装

确认 `$Stage` 是刚才那个目录；新 SSH 会话需要重新设置它。
以下复制不会覆盖原有安装。任何报错都停止，不能直接重跑或删除旧配置。

```bash
(
set -eu
sudo -v
Root=$(sudo mktemp -d /root/project1-prepare-install.XXXXXXXX)
for Name in project1_migration_prepare.py project1_deploy.py project1_migration_status.py \
    migration_package.py verify_ci_artifacts.py install_migration_prepare.py \
    install_migration_prepare.sql project1-migration-prepare project1-migration-prepare.sudoers
do
    sudo install -m 0644 -o root -g root -- "$Stage/$Name" "$Root/$Name"
done
sudo env -i PATH=/usr/bin:/bin HOME=/root LANG=C.UTF-8 \
    /usr/bin/python3 -I -c 'import sys; sys.path.insert(0, sys.argv[1]); from install_migration_prepare import main; sys.argv = sys.argv[:1]; sys.exit(main())' "$Root"
)
```

安装器通过交互提示要求输入 `sa` 密码，使用后不保存。新生成的专用备份密码只保存在
`/etc/project1-backup/password`（root:root、0600，目录 0700），不输出到日志。
真实密码不传入 `-P`、Git、GitHub secrets 或聊天；不要上传 root 安装目录。
安装失败可能留下部分 SQL 对象或私人凭据，必须管理员检查，不自动回滚或覆盖。
如果最终 `visudo -c` 失败，先保留当前 SSH 会话和错误文件，单独检查、修复 sudoers；
不要重跑整个安装器，也不要删除已创建的 SQL 对象或密码文件。
sudoers 应来自 `project1-migration-prepare.sudoers`，不能复制同名的 shell 启动脚本。
修复时先验证候选配置，再替换已确认的错误文件，最后检查完整 sudoers 配置。

权限设计：新的 `project1_backup` 登录只获得固定备份过程的 EXECUTE；不加入 sysadmin/db_owner。
`BACKUP DATABASE` 和 VERIFYONLY 所需权限由不能通过密码登录的证书身份提供，
只在签名过程执行期间起作用。签名后移除私钥。现有 `project1_migrate` 继续只读。
固定过程只接受 32 位小写 hex token，只有固定库和固定新备份路径，不能传入任意 SQL 或其他目标。
不要直接授予备份登录 `CREATE ANY DATABASE`，也不要擅自更改签名过程。

安装后检查（不会访问业务数据或停服务）：

```bash
sudo -u project1_deploy sudo -n -- /usr/local/sbin/project1-migration-prepare --help
```

预期：`Usage: project1-migration-prepare COMMIT RUN_ID ATTEMPT TOKEN ZIP_SHA256`。
未完成安装时准备 workflow 会失败，不会绕过安装或自行获得新权限。

## 2. 每次发布：先审核，再手动停 API

记录全部成功的 main/push CI 的 run ID、attempt 和完整 commit SHA。
下载对应 SQL 包进行审核、对目标结构和数据演练；哈希检查不等于安全审查。
确认维护时间和唯一操作人，并停止其他写入者。
对于新增可空栏位，旧应用通常兼容；删除、重命名、类型改变要另行安排兼容发布顺序。

```bash
sudo systemctl stop project1-api.service
systemctl show project1-api.service --property=ActiveState --property=SubState --property=MainPID
```

需要 `inactive / dead / 0`。**不停止 mssql-server**。
如果本次不需要迁移，不停 API、不运行准备流程；改用只读检查或应用部署。

## 3. GitHub：只勾选 prepare_migrations

打开 `Project1 manual deployment`，使用 main：

- `ci_run_id`：成功 main/push CI 的 run ID，不是界面的运行序号。
- `commit_sha`：同一 CI 的完整小写 SHA。
- 只勾选 `prepare_migrations`，取消 `deploy`、`check_migrations`。

流程重新验证 CI 和安装包，然后使用现有 Tailscale/SSH 信任：

1. 检查服务器工具及 API 已停止，之后才上传。
2. 上传 ZIP 到 `/home/project1_deploy/uploads/migration-uploads/<token>/`，不覆盖。
3. 使用共享应用部署锁，复制到 root 私有目录，再核对 ZIP、四个文件、来源和清单。
4. 用原只读账号确认 migration history 是选定版本的前缀，并且确有 pending migration。
5. 再次检查 API，创建新的 COPY_ONLY/checksum 备份，执行 VERIFYONLY。
6. 核对备份文件、计算哈希、再次确认 API 和历史未变；保存操作记录并报告结果。

不执行上传的 SQL，不启动/停止 API，不部署应用，不自动恢复数据库或下载备份到 GitHub。
API 停止不能阻止其他管理员、程序或计划任务写入；管理员负责实际停写。

成功 Summary 显示：commit、CI run/attempt、SQL 路径和哈希、备份路径/哈希/大小、完成时间、pending 清单。
SQL 是 root 私有的 `/var/lib/project1-migration-prepare/<token>/migrations.sql`；
备份为 `/var/opt/mssql/backup/project1-prepare-<token>/Project1Db.bak`（mssql、0600，目录 0700）。

如果 API 运行、来源或哈希不符、没有 pending、权限不够或备份失败，流程失败并停止。
API 仍由你负责，不会为了“恢复网站”自动启动。
连接中断、超时或备份状态不确定时，先检查私有 `status.json` 及 SQL 会话。
`backing-up`/`backup-uncertain` 记录会阻止新的准备；管理员确认完成情况后才能处理该记录。
既有应用部署工具留下未解决的 `pending.json` 时，同样拒绝准备。
不要直接删除记录、备份或重试来绕过检查。

## 4. 保存服务器之外的备份

准备完成只说明服务器已经有备份。迁移前还要保留受保护的服务器外副本；
同一服务器的另一目录不算异机备份，不把备份提交到 Git 或公开上传。
将 `$BackupFile` 换成 Summary 的实际路径：

```bash
BackupFile='/var/opt/mssql/backup/project1-prepare-这里填真实token/Project1Db.bak'
DownloadDir=$(mktemp -d "$HOME/project1-backup-download.XXXXXXXX")
chmod 0700 "$DownloadDir"
sudo install -m 0600 -o "$(id -u)" -g "$(id -g)" -- "$BackupFile" "$DownloadDir/Project1Db.bak"
sha256sum "$DownloadDir/Project1Db.bak"
printf '%s\n' "$DownloadDir"
```

在电脑选择一个已限制为本人、SYSTEM 和管理员访问的私人备份目录，使用新的目标文件名，不覆盖旧备份：

```powershell
$Remote = 'shunbin@192.168.1.158:这里填真实下载目录/Project1Db.bak'
$Local = 'C:\Users\shun bin\Project1Backups\这里填私人目录\这里填本次新文件名.bak'
if (Test-Path -LiteralPath $Local) { throw '目标存在，不覆盖。' }
scp -o StrictHostKeyChecking=yes $Remote $Local
if ($LASTEXITCODE -ne 0) { throw '下载失败。' }
Get-FileHash -LiteralPath $Local -Algorithm SHA256
```

与 Summary 和 Ubuntu 哈希核对。VERIFYONLY 检查可读性/checksum，不等于实际恢复演练；
应先在独立测试库验证恢复办法，不能恢复覆盖正在使用的正式库来测试。
准备后若恢复写入，应重新安排停写及新备份。旧报告和备份不是永久有效的执行许可。

## 5. 你手动执行并验证

确认目标、文件哈希、备份和执行范围，按照[手动迁移检查单](MANUAL_MIGRATIONS.md)独立授权。
管理员通过 sudo 读取 root 私有的原始 SQL；数据库密码由交互提示输入：

```bash
SqlFile='/var/lib/project1-migration-prepare/这里填真实token/migrations.sql'
sudo sha256sum "$SqlFile"
sudo env -i PATH=/usr/bin:/bin HOME=/root LANG=C.UTF-8 \
  /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -d Project1Db \
  -C -b -I -x -X1 -f 65001 -l 15 -t 120 -i "$SqlFile"
```

大写 `-I` 打开 QUOTED_IDENTIFIER，小写 `-i` 执行文件；不拼接、改写或重新生成另一份 SQL。
`-b` 报错退出不保证整份 SQL 回滚。失败就停止，先检查实际结构、历史和数据，不盲目重跑或自动恢复。
`sa` 是现有演练用管理员账号；长期执行身份应单独审核权限，不使用备份或只读账号来执行 migration。

成功后检查 migration history、column 类型/长度/可空值、关键业务数据。
对于旧 API 兼容的新可空 column，再手动启动 API（现有应用部署工具要求 active）：

```bash
sudo systemctl start project1-api.service
systemctl is-active project1-api.service
```

然后单独触发同一 CI 版本的应用部署：只勾选 `deploy`。最后验证网页读写、刷新和清空。
不要把这个顺序套用于旧 API 不兼容的删列/改名；要先设计兼容发布。
操作 SQL、备份和记录保留，不自动清理；管理员安排容量监控及经审核的保留策略。

## 6. 本地测试及上线前验收

```powershell
python -m unittest discover -s scripts/deploy/tests -v
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj --configuration Release
```

本地 Python 测试不连接服务器；Windows 跳过少量 Linux 文件权限测试。
真实签名权限、BACKUP/VERIFYONLY 测试仅在现有一次性 CI SQL Server 容器执行，
不恢复之前耗时的 Ubuntu 宿主安装验收。必须检查新 CI 后再安装。
上线验收顺序：API 运行时准备应拒绝 → 安排维护并停止 API → 有真实 pending 时准备成功 →
核对 Summary 和服务器记录 → 手动迁移。不能用伪造生产 history 来制造 pending。

参考：[证书签名权限](https://learn.microsoft.com/en-us/sql/relational-databases/tutorial-signing-stored-procedures-with-a-certificate)、
[VERIFYONLY 权限与限制](https://learn.microsoft.com/en-us/sql/t-sql/statements/restore-statements-verifyonly-transact-sql)、
[COPY_ONLY](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/copy-only-backups-sql-server)。
