# Ubuntu Docker 正式接管：保留原 Project1Db

这是 Phase 5 的**首次手动切换**，不是自动 Docker CI/CD，不执行数据库迁移，也不是公网
HTTPS 配置。先读完流程并审阅脚本，每次只执行当前步骤。任何 STOP 都先检查状态，不
删除状态文件、不覆盖私密文件、不重复执行失败的变更。

首次现场在 SQL 备份权限 `660` 处停止，已回退后使用修正版重新准备。第二次新备份／
恢复／异机副本已验证，但 activate 记录为 `activation-failed`。随后两个容器、原网址、
数据库保护检查及浏览器登录／旧数据读取都通过，初次失败的具体原因尚未确定。
**不要重跑 stage/configure/freeze/activate 或手改状态。** 当前使用
[已运行部署的受控复核](ubuntu-docker-activation-recheck.md)，而不是重新启动应用。
[回退后重试指南](ubuntu-docker-production-retry.md) 保留作为之前步骤的历史记录。

## 范围、停机和现状

- 已由用户提供的现场输出确认：`homelab-server` / Ubuntu 24.04 / x86_64，原 API
  `project1-api.service` active/enabled，监听 `127.0.0.1:5000`；Nginx 监听 80；
  SQL Server 使用 1433；练习网页使用 loopback 8083。不是助手实时登录得到的状态。
- 用户已批准目标：Docker 版正式替换旧部署，继续使用原 `Project1Db`；保留原网址、
  原用户和密码，不合并练习库数据。开始前仍需重新确认现场状态。
- 只改 `/etc/nginx/sites-available/project1`，保留 `default` 站点及原 server_name：
  `192.168.1.158 localhost 127.0.0.1`。宿主 Nginx 的 80 转发到本机 Docker 网页
  `127.0.0.1:8084`，再由容器 Nginx 转发 API。API 不发布宿主端口。
- 使用新的 root-only 配置和原库受限专用 SQL 账号 `project1_docker_production`，
  **不把 sa 交给 API**，不复制或读取旧 `/etc/project1/project1.env`。JWT 是新密钥，
  现有用户需重新登录；这不是密码重置。
- 关闭 `DemoUsers__Enabled`，不创建／重设 demo 账号。角色、邮件模板和工作流的既有
  startup 初始化仍可能写入缺失数据；邮件后台任务会运行，启动不是只读操作。
- `freeze` 开始维护停机：先禁用旧 API 自启并停止它，避免维护期间重启主机又启动旧
  写入者；回退会恢复其 enabled 状态。SQL Server 和 Nginx 不停止。
- `activate` 之前必须保持所有原库写入停止，完成新鲜备份、实际恢复演练、受保护的
  异机副本下载。**不是零停机切换**，下载／排查期间旧 API 都保持停止。
- 练习配置、练习库、旧 API/前端目录、旧 env 和镜像均保留，不做自动清理。
- 助手未连接真实 Ubuntu 执行操作。当前用户输出确认修正版 Docker 应用已运行并通过
  登录／旧数据读取；部署记录尚待受控复核，业务闭环／PDF／邮件和 finalize 尚未完成。

## 文件和权限

| 路径 | 用途 |
| --- | --- |
| `compose.ubuntu.yml` | 正式版两服务，Production，原库，禁用 demo；无 migrate profile |
| `.env.docker.ubuntu.example` | 无工作凭据的参考模板，不是可启动配置 |
| `Export-UbuntuProduction.ps1` | 电脑构建两个 runtime 镜像和完整性清单，不 SSH/SQL |
| `Receive-UbuntuProductionBackup.ps1` | 电脑下载备份到只有本人/SYSTEM/管理员可访问的新目录，验证大小和 SHA256 |
| `ubuntu_production.py` | 固定主机首次切换状态机；所有变更都需 `--apply` |
| `/home/shunbin/project1-ubuntu-production` | 上传的公开发布文件，shunbin，0700 |
| `/usr/local/libexec/project1-docker.py` | 经审阅安装的 root-owned helper，0644；用 `python3 -I` 隔离运行 |
| `/opt/project1-docker/releases/<tag>` | root-only 封存发布文件；不从用户可写上传目录运行 root Compose |
| `/etc/project1-docker/project1.env` | 新正式配置，root:root，0600，父目录 0700 |
| `/var/lib/project1-docker/state.json` | 私有阶段／备份元数据，不含密码；不要删除或手工改阶段 |
| `/opt/project1-docker/old-nginx.conf` | 现场 Nginx 原始字节的 root-only 快照；不是模板替代品 |

不要使用 Windows 私密 `.env` 或旧练习凭据，不把密钥作为 build args，不贴完整
`docker inspect`、Compose `config`、私密 `.env` 或密码。只使用 `config --quiet`。
root helper 只能由拥有正常 sudo 权限的管理员手动调用，不给 CI 配置任意 Python／Docker
的免密码 sudo 权限。旧部署与新切换工具共用旧 deploy.lock，避免并行切换。

## 0. 本地测试【电脑 PowerShell，仓库根目录】

```powershell
python -m unittest discover -s scripts/docker/tests -p 'test_ubuntu*.py' -v
dotnet test tests/Project1.Api.Tests/Project1.Api.Tests.csproj --configuration Release
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj --configuration Release
dotnet list backend/Project1.Api/Project1.Api.csproj package --vulnerable --include-transitive
```

默认 Python 测试只检查假配置、保护逻辑和命令顺序，实际 SQL 测试须显式启用。
真实 Ubuntu 的 systemctl/Nginx/目录权限切换没有被模拟单元测试证明可用。

## 1. 构建正式发布包【电脑 PowerShell】

Docker Desktop 必须 Engine running，确认当前 context 是电脑的 `desktop-linux`。

```powershell
docker context show
docker version
& .\scripts\docker\Export-UbuntuProduction.ps1
& .\scripts\docker\Export-UbuntuProduction.ps1 -Apply
```

不加 `-Apply` 只显示预览。实际执行会构建两个 linux/amd64 非 root runtime 镜像，
用隔离、无网络的一次性容器读取公开 index.html 哈希，然后导出 `images.tar`。
它不启动业务 API、不连接任何数据库、不发送邮件。SDK/迁移镜像不进入正式发布包。

输出 `ubuntu-production-release/prod-时间-随机值/`；每次新标签，不覆盖旧包。
清单含镜像 ID/运行 UID、全部迁移 ID、前端入口哈希、镜像包及五个公开文件的 SHA256。
这是人工信任的本机构建，不是受 CI 签名验证的包；完整性哈希不独立证明来源。

## 2. 准备独立上传目录【Ubuntu SSH】

先核对 `hostname`、`id -un`、`free -h`、`df -h /`、旧服务状态、`sudo nginx -t` 和
`sudo ss -lntp`。8084 必须空闲，不为此停止其他程序或开放 SQL 到公网。

```bash
(
set -eu
Dir=/home/shunbin/project1-ubuntu-production
if [ -e "$Dir" ] || [ -L "$Dir" ]; then
    printf 'STOP：上传目录已存在，先审阅，不覆盖。\n'
    exit 1
fi
umask 077
mkdir -m 0700 -- "$Dir"
)
```

## 3. 上传八个公开文件【电脑 PowerShell】

`$Release` 使用本次实际导出目录；SSH 地址以你核对过的服务器为准，保持主机身份检查。

```powershell
& {
    $ErrorActionPreference = 'Stop'
    $Release = 'REPLACE_PRINTED_PRODUCTION_RELEASE_FOLDER'
    if (!(Test-Path -LiteralPath (Join-Path $Release 'release.json'))) { throw '先填写实际发布目录。' }
    scp -o StrictHostKeyChecking=yes `
        "$Release\images.tar" "$Release\images.tar.sha256" "$Release\release.json" `
        "$Release\compose.ubuntu.yml" "$Release\.env.docker.ubuntu.example" `
        "$Release\ubuntu_production.py" "$Release\project1-docker.conf" "$Release\project1-legacy.conf" `
        shunbin@192.168.1.158:/home/shunbin/project1-ubuntu-production/
    if ($LASTEXITCODE -ne 0) { throw '传输失败，停止。' }
}
```

## 4. 校验、加载、安装 root 工具【Ubuntu SSH，尚未停机】

先运行只读校验，任一失败就停止：

```bash
(
set -eu
cd /home/shunbin/project1-ubuntu-production
chmod 600 -- images.tar images.tar.sha256 release.json compose.ubuntu.yml \
    .env.docker.ubuntu.example ubuntu_production.py project1-docker.conf project1-legacy.conf
python3 ubuntu_production.py archive
sha256sum -c images.tar.sha256
)
```

通过后单独加载，`load` 不启动应用：

```bash
cd /home/shunbin/project1-ubuntu-production
sudo docker load --input images.tar
```

审阅上传的 Python 脚本后，安装一次 root 工具，不覆盖已有安装：

```bash
(
set -eu
if [ ! -d /usr/local/libexec ]; then
    sudo install -d -m 0755 -o root -g root /usr/local/libexec
fi
[ "$(readlink -f /usr/local/libexec)" = /usr/local/libexec ] || exit 1
[ "$(stat -c '%U:%G:%a' /usr/local/libexec)" = root:root:755 ] || exit 1
if [ -e /usr/local/libexec/project1-docker.py ] || [ -L /usr/local/libexec/project1-docker.py ]; then
    printf 'STOP：root 工具已存在，先审阅，不覆盖。\n'
    exit 1
fi
sudo install -m 0644 -o root -g root \
    /home/shunbin/project1-ubuntu-production/ubuntu_production.py \
    /usr/local/libexec/project1-docker.py
)
```

`python3 -I` 防止从用户目录导入 Python 模块；不要用普通上传目录脚本直接执行 root
变更，也不要修改已封存的文件来绕过哈希检查。

## 5. 封存发布和配置账号【Ubuntu SSH，旧服务仍运行】

先预览，再按明确步骤执行：

```bash
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py stage
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py stage --apply
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py configure
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py configure --apply
```

`stage` 核对旧服务/旧网页健康、站点链接、8084、镜像 ID/UID/文件哈希，保存真实旧
Nginx 字节和旧 DLL/index/unit 哈希，封存 root-only 文件，不启动容器、不执行 SQL。

`configure` 交互要求：Ubuntu SQL sa 密码（隐藏输入、不保存），生产 Gmail 地址和
Google App Password（隐藏输入、去掉显示空格）。Gmail 不做自动认证／试发。
它只读核对原库全部迁移记录和 Products 字段；不一致直接停止，**不会迁移**。
然后生成新的运行账号密码、JWT、只用于“新增用户”操作的默认密码，并新建 SQL 登录／
数据库用户，授予原库 `db_datareader` / `db_datawriter`。这是 SQL 权限元数据变更，
不是重建库、修改原业务用户或重设其密码。

现有旧 SQL 账号、旧 env 和旧应用文件不修改。若账号已存在、配置/状态已存在或部分
创建失败，保留现场，不通过删文件／重新生成密码来“重跑”。此时旧 API 尚未停机。

## 6. 进入维护窗口、停止写入并备份【Ubuntu SSH，会停旧 API】

通知使用者停止操作，关闭所有连接原 `Project1Db` 的其他客户端（包括 SSMS）、
导入程序和其他写入者，确认没有 Pending 邮件。不要同时触发旧手动 CD，也不要重启
服务器／SQL 服务。`--writers-stopped` 是操作者声明，不是工具能证明所有外部写入已停。

```bash
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py freeze
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py freeze --apply --writers-stopped
```

实际操作顺序：

1. 在停止旧服务前检查环境、原库迁移/权限/队列、备份父目录和磁盘空间。
2. 记录中断状态，禁用旧 API 自启，再停止它；要求 inactive / MainPID=0 / disabled。
3. 拒绝原库还有其他普通客户端连接的情况；不自动踢连接或杀进程。
4. 做唯一新文件的 `COPY_ONLY, CHECKSUM` 备份，执行 `RESTORE VERIFYONLY`。
5. 再恢复到唯一 `Project1DockerRestore_<随机值>`，两个文件 MOVE 到独立新目录，
   不使用 WITH REPLACE，不覆盖原数据库／数据文件。恢复副本设 RESTRICTED_USER、
   READ_ONLY，执行 CHECKDB、核对迁移和用户/产品/订单/邮件数量。
6. 保留备份、恢复副本和状态；生成 shunbin 可下载的 0700 目录、0600 备份副本。

恢复演练库只是校验备份，不是新业务练习环境；正式 API 仍固定连接原 `Project1Db`。
数量比较不是全部业务字段逐值比对，VERIFYONLY 也不能替代实际恢复，故两者分开执行。
当前工具只接受两个 D/L 文件、简单逻辑文件名的原库备份；遇到额外文件、TDE 等不同
布局不猜测、不自动处理，停止并另行审核。

成功输出下载路径、SHA256、大小。**这时原 API 已停机，网站 API 暂不可用。** 失败也
不能假设旧服务仍在运行；先看 `status`，需要时走第 9 步回退，不盲目重跑 freeze。

## 7. 下载受保护异机备份【电脑 PowerShell，旧 API 保持停止】

用第 6 步实际打印值替换占位符，不是旧备份、练习备份或代码中的假配置：

```powershell
& .\scripts\docker\Receive-UbuntuProductionBackup.ps1 `
    -RemoteFile 'REPLACE_PRINTED_DOWNLOAD_FILE' `
    -ExpectedSha256 'REPLACE_PRINTED_SHA256' `
    -ExpectedBytes REPLACE_PRINTED_BYTES
```

该命令在 `~/Project1Backups/ProductionCutover-<GUID>/` 新建受限目录，下载原库备份，
核对哈希及长度。**备份包含真实业务数据，不能上传 Git、聊天或公开云盘。**
本机磁盘副本不代表已有离线／异地灾备策略；后续仍需保护、保留和定期演练。

若确认下载失败，保持维护，排查或明确回退；不要拿未校验副本的 SHA/大小直接执行激活。

## 8. 激活并验证原网址【Ubuntu SSH + 浏览器】

仅在异机副本验证成功、所有写入仍停止后，用你电脑确认过的真实 SHA256/字节数：

```bash
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py activate
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py activate --apply --writers-stopped \
    --backup-sha256 REPLACE_VERIFIED_SHA256 --backup-bytes REPLACE_VERIFIED_BYTES
```

工具再次核对旧 API 已停止/disabled、旧文件和站点未变化、备份未变、迁移/权限/
Pending/数量符合冻结时状态。它无法远程证明电脑上的副本真实存在，传参表示操作者
已完成上一步的本地核验，不要把参数当作绕过保护的开关。
此步骤会再次隐藏提示输入 Ubuntu SQL sa 密码（不保存），核对原库没有其他普通客户端
连接；不要在下载期间恢复业务或重新打开 SSMS。

启动正式两个容器并等待健康，再检查新网页/API和 index 哈希，才替换 Project1 站点、
执行 `nginx -t` 并 reload（不是 stop/restart），最后检查原 80 端口。`default` 不修改。
任一步失败记录 `activation-failed`，不自动启动旧 API，也不自动恢复数据库，按第 9 步
人工回退。不要重新运行 activate 试运气；完整状态／文件保留用于排查。

成功后从原网址 `http://192.168.1.158` 登录：用**原数据库的账号和原密码**，不是新练习
密码，也不是 `PROD_NEW_USER_PASSWORD`。前端 demo 按钮的默认密码不能替代原密码。

在恢复正常业务前验证：

- 原有用户、角色和业务记录正确，登录/退出/刷新正常。
- 仅创建可识别的测试记录，验证保存；不要删除真实业务记录。
- 手动生成 PDF 检查内容和字体。
- 只新建一封发往自己控制地址的邮件，检查 To/CC/BCC 和 Gmail 收件箱、PDF 附件；
  不盲目重发旧供应商订单。Sent 仅代表 SMTP 接受。
- 新容器正常、旧 API inactive/disabled，SQL Server 未被容器化或重启。

账号登录、数据、PDF、邮件实际通过后，明确记录完成：

```bash
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py finalize --apply
```

不再使用练习环境时，另行停止**练习项目**即可，不删练习库、私密文件或镜像。
新的正式密码／JWT 只在 root-owned 私密文件中；不要贴出内容。已发出的邮件和正式
业务写入不会被代码回退撤销。

## 9. 状态、失败和显式回退【Ubuntu SSH】

只查看阶段、标签和备份元数据（无密码）：

```bash
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py status
```

- `staged` / `configured`：尚未停止旧 API；不需要恢复原库。
- `configuring`：账号创建可能部分完成，旧 API 尚未停机；人工检查，不删除状态重跑。
- `freezing` / `frozen`：旧 API 可能已 disabled/停止，备份可能未完成，保留现场。
- `backup-ready`：旧 API 停止、等待异机副本验证和激活。
- `activating` / `activation-failed`：新容器和 Nginx 可能已部分切换，不能直接启动旧 API。
- `active-pending-verification` / `active`：Docker 服务原网址，旧 API 保持停止。
- `rolling-back` / `rollback-failed`：回退未确认完成，不能假设服务健康，需人工排查。
- `rolled-back`：旧站恢复；如需重试，使用单独指南的 `prepare-retry`，不删除状态或重新 configure。
- `retry-preparing`：重试准备可能部分完成；旧站未被该命令停止。保留历史配置／状态快照，
  先审阅现场，不能盲目重跑、手改阶段或执行 freeze。

回退也先停止所有业务操作／写入，再显式执行：

```bash
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py rollback
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py rollback --apply --writers-stopped
```

回退先停正式 Docker 容器并确认停止；如果 Docker 无法确认新写入者已停，就不会启动
旧 API。核对原库 schema、Pending 邮件、保留的旧 DLL/index/unit 以及 Nginx 快照，
再恢复旧站点原始字节、校验配置、恢复旧服务 enabled/start、reload Nginx，并检查旧
页面和 API。它不更改旧应用文件、不删除新 SQL 账号、不还原任何数据库、不撤销邮件。

若 Pending 邮件、schema 或文件发生变化，回退可能拒绝继续，必须先审核；切勿直接
`systemctl start` 绕过两套后台任务保护。应用发送邮件和数据库记录之间没有 exactly-once
投递保证，中断／重试尤其需要审查邮件状态，不把自动重试当作修复。

这些工具仅处理本次已审阅的首次切换；断电/kill 无法保证自动恢复。保留全部状态、
备份、恢复副本、下载副本、旧应用和镜像，不运行 prune／强制删除来“清理”。之后的
版本更新、schema 迁移、DB 恢复、公网 HTTPS 和 Docker CI/CD 要分别制定方案。
旧非 Docker 手动部署流程不再用于更新网站，避免重新启动旧服务；现有 CI 测试可保留。
经显式回退后的工具修复重试是单独的受控路径，不是任意版本升级；必须重新进入维护窗口、
生成新鲜备份、恢复演练、验证异机副本，旧备份不能授权本次激活。

## 验证边界

2026-10-19 本地验证记录（不是正式服务器验收）：

- Python 默认套件：74 项通过，3 项需要显式启用的 SQL 测试跳过。
- 另行显式运行这 3 项隔离 Docker SQL 测试：全部通过，覆盖备份／实际恢复／CHECKDB、
  原库仍可写而恢复副本只读、备份下载副本权限、受限账号及过宽权限拒绝、正式镜像启动、
  前端代理、原有用户密码哈希保留，以及宿主 Nginx 模板语法。
- .NET：API 测试 163 项通过；迁移测试 62 项通过，原有 9 项 SQL 集成测试未启用而跳过。
- PowerShell 脚本语法检查通过；没有连接正式服务器、发送真实邮件或执行正式切换。

上面是最初一轮测试记录，没有模拟现场 SQL Server 的 `660` 输出，随后暴露了权限检查
顺序缺陷。修正版补充的复现和重试测试见单独重试指南；不能把历史绿灯当作该缺陷不存在。

本地测试使用私有隔离 Docker 网络、无宿主发布端口、随机标签的临时 SQL Server、
公开假密码和测试数据；不读任何实际 `.env`、旧配置或远程服务器。测试结束只停止并
移除带本次随机所有权标签的资源，不 prune 用户资源。
真实服务器 root 路径、systemd、备份目录、其他客户端和现场 Nginx 必须在实际步骤中
再次检查。Healthy、单元测试、测试库恢复成功都不等同于正式业务已通过。

参考：[Docker Compose 健康依赖](https://docs.docker.com/compose/how-tos/startup-order/)、
[发布端口安全](https://docs.docker.com/engine/network/port-publishing/)、
[Nginx reload](https://nginx.org/en/docs/control.html)、
[SQL BACKUP](https://learn.microsoft.com/en-us/sql/t-sql/statements/backup-transact-sql)、
[VERIFYONLY 的边界](https://learn.microsoft.com/en-us/sql/t-sql/statements/restore-statements-verifyonly-transact-sql)、
[Google App Password](https://support.google.com/mail/answer/185833)。
