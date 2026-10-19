# Ubuntu Docker 练习：手动部署 Vue + API

本指南属于 Phase 5，不是 GitHub Actions 自动部署，也不是正式环境切换。
每次只执行当前步骤；遇到 STOP、SQL 错误、网络或端口冲突先停下，不反复重试。

## 范围与当前状态（2026-10-19）

- 用户的服务器为 `homelab-server`、Ubuntu 24.04、x86_64；Docker 29.9.0、
  Compose 5.6.0、`hello-world` 已由用户的终端输出确认。
- 最近一次用户输出：原 API 在 `127.0.0.1:5000`，Nginx 使用 `80`，SQL Server 使用
  `1433`；部署前仍需重新检查。这不是助手实时登录服务器得到的状态。
- 初期准备后，用户已完成 `practice-20261019t110519z-c2df35f3` 的镜像构建／导出／上传／
  校验／加载；服务器空练习库和专用账号已创建，迁移清单核对通过，两个容器 Healthy，
  通过 SSH 隧道在 8083 实际登录成功。尚未验证此 Ubuntu 练习环境的真实 Gmail/PDF；
  公网、HTTPS、重启整个服务器的演练均未进行。这不代表原库正式切换完成。
- 常驻容器只有 Vue/Nginx 和 API。沿用 Ubuntu 现有 SQL Server 服务，但新建空的
  `Project1DockerUbuntuPracticeDb`；不复制正式数据，不连接原 `Project1Db`。
- 不停止原 API、不改原 Nginx 配置、不重启 SQL、不修改监听地址或防火墙。
- 网页只绑定 `127.0.0.1:8083`；API 没有宿主端口。通过 SSH 隧道从电脑访问。
- 全新练习账号与 JWT 密钥只在服务器生成。Windows 私密 `.env`、User Secrets、
  原服务器 `/etc/project1/project1.env` 均不能复制进镜像、发布包、Git 或聊天。

## 新文件的作用

| 文件 | 作用 |
| --- | --- |
| `compose.ubuntu-practice.yml` | 启动已加载的版本化应用镜像；独立项目名，固定练习库和账号 |
| `.env.docker.ubuntu-practice.example` | 无工作凭据的参考模板，不是可直接启动的配置 |
| `scripts/docker/Export-UbuntuPractice.ps1` | 在电脑构建 linux/amd64 镜像并导出 tar、哈希和发布清单；不 SSH、不启动容器 |
| `scripts/docker/ubuntu_practice.py` | 在指定 Ubuntu 目录中，分开执行校验、初始化、初次迁移、可选 Gmail 配置 |

`migrate` 是 tools profile 中的一次性工具，不会被普通 `compose up` 启动。
API 使用受限读写账号；迁移账号只拥有练习库的 db_owner，不交给 API。`sa` 密码只在
初始化时交互输入，通过子进程环境传给 native sqlcmd，不保存、不作为命令行参数。

## 0. 本地验证与构建前提【电脑 PowerShell】

先打开 Docker Desktop，等到 Engine running。在仓库根目录运行：

```powershell
docker version
python -m unittest discover -s scripts/docker/tests -p test_ubuntu_practice.py -v
dotnet test tests/Project1.Api.Tests/Project1.Api.Tests.csproj --configuration Release
dotnet test tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj --configuration Release
dotnet list backend/Project1.Api/Project1.Api.csproj package --vulnerable --include-transitive
```

Python 测试使用公开的假配置验证 Compose，不读取你的私密文件，不需要 Docker 引擎。
SQL Server 集成测试没有显式测试环境时会跳过；跳过不代表实际 SQL 迁移通过。
已针对 SqlClient/PDFsharp 间接依赖的 `System.Security.Cryptography.Pkcs` 9.0.4
添加同主版本补丁 9.0.21，不能用旧镜像代替重新构建。

## 1. 构建并导出镜像【电脑 PowerShell】

先看预览，再显式执行：

```powershell
& .\scripts\docker\Export-UbuntuPractice.ps1
& .\scripts\docker\Export-UbuntuPractice.ps1 -Apply
```

- 不带 `-Apply` 只显示说明。
- `docker build --pull --platform linux/amd64`：刷新基础镜像并为服务器架构构建。
- `--target runtime` 是小型运行镜像；`--target migrations` 是一次性 EF 工具镜像。
- `docker image save`：把三个镜像保存到 `images.tar`，不是运行或上传。
- 每次产生新的 `practice-时间-随机值` 标签，不覆盖上一版标签。
- 输出目录为 `ubuntu-practice-release/<本次标签>/`，被 Git 忽略。清单记录各镜像 ID、
  tar 的 SHA256、本次全部迁移 ID 和三个公开配置／工具文件的 SHA256；
  服务器使用前会拒绝被意外修改的配置／工具。只输出公开的路径、标签和哈希。

原有两份 Compose 和本机正在使用的容器不会被该脚本启动、停止或重建。
构建目录采用已有 `.dockerignore` 白名单，私密配置不作为 build args。
如果构建失败，保留现场检查；不要把不完整目录上传。此步骤尚未自动提交或推送代码。

## 2. 检查服务器并准备独立目录【Ubuntu SSH】

```bash
hostname
id -un
free -h
df -h /
systemctl show project1-api.service --property=ActiveState --property=SubState --property=MainPID
systemctl is-active mssql-server
sudo ss -lntp
sudo ufw status
```

确认是 `homelab-server` / `shunbin`，原服务正常，新 `8083` 未被占用。防火墙状态只是
查看；不要运行 `ufw disable`、开放公网 SQL 或盲目添加规则。Docker 的发布端口可能
绕过 UFW，所以本练习明确只绑定 loopback。

确认目录尚未使用后创建，若已存在则停止，不覆盖：

```bash
(
set -eu
Dir=/home/shunbin/project1-ubuntu-practice
if [ -e "$Dir" ] || [ -L "$Dir" ]; then
    printf 'STOP：目录已存在，先检查，不覆盖。\n'
    exit 1
fi
umask 077
mkdir -m 0700 -- "$Dir"
)
```

`0700` 表示只有当前用户能进入。该固定目录和用户／主机名受脚本检查，不能把脚本
直接用于其他服务器或用 `sudo python3` 执行。

## 3. 只传发布包【电脑 PowerShell】

`$Release` 替换为导出命令实际打印的本次目录，SSH 地址使用你确认过的服务器地址。
以下例子沿用 `192.168.1.158`；如果地址已变化，先确认再替换：

```powershell
$Release = 'REPLACE_PRINTED_RELEASE_FOLDER'
if (!(Test-Path -LiteralPath (Join-Path $Release 'release.json'))) {
    throw '先填写本次完整发布目录。'
}
scp -o StrictHostKeyChecking=yes `
    "$Release\images.tar" "$Release\images.tar.sha256" "$Release\release.json" `
    "$Release\compose.ubuntu-practice.yml" "$Release\ubuntu_practice.py" `
    "$Release\.env.docker.ubuntu-practice.example" `
    shunbin@192.168.1.158:/home/shunbin/project1-ubuntu-practice/
if ($LASTEXITCODE -ne 0) { throw '传输失败，停止。' }
```

`scp` 是复制文件；这里只列出 6 个公开发布文件，不上传整个仓库或任何私密文件。
`StrictHostKeyChecking=yes` 拒绝未经确认的服务器身份；连接失败时不要随意关闭校验。

## 4. 校验并加载，不启动应用【Ubuntu SSH】

```bash
cd /home/shunbin/project1-ubuntu-practice
python3 ubuntu_practice.py archive
sha256sum -c images.tar.sha256
```

两次校验都通过，才另行执行：

```bash
sudo docker load --input images.tar
```

`load` 只是把镜像装进服务器 Docker，不创建数据库、不启动 API。哈希校验用于确认
传输完整性，不是独立的代码签名；发布包仍依赖你信任的本地构建和已验证的 SSH。

## 5. 只创建新空库和账号【Ubuntu SSH】

先预览：

```bash
python3 ubuntu_practice.py init
```

检查完脚本并确认执行本步骤后：

```bash
sudo -v
python3 ubuntu_practice.py init --apply
```

在终端隐藏提示中输入现有 Ubuntu SQL 的 `sa` 密码，不要把密码写进命令或聊天。
`sudo -v` 只刷新 sudo 授权缓存，供脚本调用 `sudo -n docker` 检查已加载镜像。

脚本只会：

1. 核对主机、目录、发布包、镜像 ID，确认新库和两个账号均不存在。
2. 生成新的随机密码、JWT 和 demo 密码，写入 owner-only `0600` 私密文件。
3. 新建空 `Project1DockerUbuntuPracticeDb` 及两个专用 SQL 登录／用户。
4. 验证运行账号只能读写练习库，不能访问原 `Project1Db`、没有 DDL/管理员权限。

它不备份／修改／还原原库，不切换 SQL 登录模式，不开放端口，不迁移、不发信。
新建空库前不需要重做原数据库的迁移备份；这不是对原库实施变更。
如果中途失败，私密文件和状态会保留，禁止通过删除文件来强行重跑。脚本没有自动
删除库、删除账号、恢复备份或清理容器的功能。先人工检查创建到了哪一步。

生成的两个私密文件分别是：

- `.env.docker.ubuntu-practice.local`：只给运行环境，邮件初始为不能外发的 loopback port 1。
- `.env.docker.ubuntu-practice.migrate.local`：只在显式迁移时读取的练习库权限账号密码。

不要 `cat`、截图或把它们上传。参考 `.example` 仍然没有工作凭据。

## 6. 显式执行初次迁移【Ubuntu SSH】

```bash
sudo -v
python3 ubuntu_practice.py migrate
python3 ubuntu_practice.py migrate --apply
python3 ubuntu_practice.py check
```

预览不变更。`migrate --apply` 先拒绝已运行的练习应用或非空练习库，再启动一次性
EF 工具容器执行已有 migrations，完成后移除工具容器，核对全部迁移 ID。不是删库
重建，也不是对原库再执行删列。容器通过显式 `host-gateway` 访问宿主 SQL Server。
网络或 SQL 权限失败时停止，不自动修改防火墙、不切换目标到 `Project1Db`。

`check` 是本机 SQL 只读检查，不单独证明 Docker 网络、PDF、SMTP 或网页已通过。
首次成功后不要重复执行迁移命令；它刻意只接受空库，后续数据库升级另行审阅。

## 7. 启动两个应用容器【Ubuntu SSH】

```bash
sudo docker compose --env-file .env.docker.ubuntu-practice.local -f compose.ubuntu-practice.yml config --quiet
sudo docker compose --env-file .env.docker.ubuntu-practice.local -f compose.ubuntu-practice.yml up -d --wait
sudo docker compose --env-file .env.docker.ubuntu-practice.local -f compose.ubuntu-practice.yml ps
```

- `config --quiet` 校验配置但不输出密码；不要运行完整 `config` 或完整 `docker inspect`。
- `up` 创建／启动容器；`-d` 在后台运行；`--wait` 等待启动和健康检查。
- 默认不启动 tools profile；运行容器没有迁移密码，启动入口不执行迁移。
- `ps` 查看这个项目的状态。项目名固定为 `project1-ubuntu-practice`，不操作原服务。
- 首次 API 会向练习库写入角色、demo 账号和模板；本步骤并不是只读操作。
- 开发 HTTP 健康接口只用于私有练习，Healthy 不等于所有业务功能正常。

不要省略或混用 `--env-file` / `-f`，也不要继承其他终端自行设置的 `PRACTICE_*` 变量。

## 8. 用 SSH 隧道访问【电脑另一个 PowerShell】

```powershell
ssh -o StrictHostKeyChecking=yes -o ExitOnForwardFailure=yes `
    -N -L 127.0.0.1:8083:127.0.0.1:8083 shunbin@192.168.1.158
```

`-L` 把电脑本机 8083 转发到服务器本机 8083；`-N` 不打开远程 shell，终端保持运行。
`ExitOnForwardFailure` 防止本地端口占用却误以为隧道成功。电脑和服务器的 8083 都要空闲。
浏览器访问 `http://127.0.0.1:8083`。关闭这条 SSH／Ctrl+C 只关闭隧道，不停止服务器容器。

登录用 `admin@demo.local` 和服务器新生成的练习密码，不是 Windows 原账号密码。
需要取密码时，在服务器交互式私密编辑器打开 `.env.docker.ubuntu-practice.local`，
只在你自己的屏幕查看 `PRACTICE_DEMO_PASSWORD`，不要输出到共享日志／聊天。
不使用前端硬编码的 “Use account” 密码，不把真实 demo 密码打包到 Vue。

手动验证：分类、单位、产品创建／编辑／刷新；PDF 生成和附件内容；浏览器未登录访问
API 应返回 401。这里的空库没有原来的业务数据是预期行为。

## 9. 单独启用 Gmail 并验证收件【Ubuntu SSH + 电脑浏览器】

初始配置不能外发邮件；此时点击发送会失败，这是预期保护，不是 Gmail 已配置。
先完成基础验证。启用 Gmail 前停止的只是本练习项目，原 API 继续运行：

```bash
sudo docker compose --env-file .env.docker.ubuntu-practice.local -f compose.ubuntu-practice.yml down
sudo -v
python3 ubuntu_practice.py gmail
python3 ubuntu_practice.py gmail --apply
```

脚本要求本练习项目没有运行中的容器、没有 Pending 邮件，然后交互输入你的 Gmail
地址和 Google App Password。密码隐藏输入，可去掉显示空格；不是普通 Google 密码。
只更新本练习私密配置，不读取原 SMTP 配置，不验证 SMTP 登录，也不自动发送邮件。
如果有 Pending，先审阅队列，不删除记录来绕过检查。

重新运行 `check`、第 7 步启动。由你在网页**新建一封**收件人／CC／BCC 均为你自己
控制地址的测试邮件，再核对 Gmail 收件箱和 PDF。不要盲目重试旧业务邮件；旧邮件的
From、收件人、正文和附件是历史快照，不会被新 SMTP 配置自动改写。
`Sent` 只代表 SMTP 接受；收件箱实际收到才算投递测试通过。

## 10. 停止、恢复、持久性与回退

只停止／移除本练习应用与网络【Ubuntu SSH】：

```bash
sudo docker compose --env-file .env.docker.ubuntu-practice.local -f compose.ubuntu-practice.yml down
```

再用第 7 步启动，检查原测试产品仍存在。这验证宿主 SQL 数据持久性，不是备份。
`down` 不删除镜像或宿主数据库，不停止原 API／SQL，也不会撤销已经发出的邮件。
不使用 `down -v`、`docker system prune`、强制删除或重启整个服务器来排查问题。

后续更新先在电脑生成新标签发布包，在服务器新的受保护暂存目录单独校验后加载。
保留旧镜像、清单和私密配置的受保护副本，再审阅迁移差异。不要覆盖当前发布清单后
重跑 `init`；现有配置也不能重新生成密码。

- 仅应用代码变化且 schema 兼容：停止本练习项目，人工审阅后一起更新镜像标签和清单，
  保持原密码，执行只读检查再启动。旧镜像不能直接根据标签可用就断言兼容。
- schema 变化：另行制定练习库备份、恢复演练、迁移审核和权限安排，不复用“初次空库迁移”。
- 回退只限于 schema 兼容的上一版镜像；回退镜像不会恢复被删列或数据。若不兼容，先停
  本练习应用并保留现场，单独审阅数据库恢复，不能还原到原 `Project1Db`。
- 新练习库有需要保留的数据后，必须安排单独 `COPY_ONLY, CHECKSUM` 备份、校验和异机
  保留／恢复演练。此指南不自动执行备份或声称已有可恢复备份。
- 原 `project1-api.service`、Nginx 和原 `Project1Db` 保持原状，正式切换需要新的明确批准。

## 验证记录

2026-10-19 源码验证：API 163 项通过；离线迁移项目 62 项通过、9 项 SQL 集成测试跳过；
新增 Python/Compose 安全契约测试通过；NuGet 当前源审计未报告 API 的已知漏洞。
初期准备时电脑 Docker 引擎未运行；之后用户已完成该版本构建、服务器迁移和应用启动，
并提供两个容器 Healthy、8083 登录成功的输出／截图。其他完整业务、邮件和 PDF 测试
不能据此标记通过。原 `Project1Db` 的正式接管使用另一份专用方案，不修改本练习配置。

参考：[Docker 导出镜像](https://docs.docker.com/reference/cli/docker/image/save/)、
[加载镜像](https://docs.docker.com/reference/cli/docker/image/load/)、
[host-gateway](https://docs.docker.com/reference/cli/docker/container/run/#add-entries-to-container-hosts-file---add-host)、
[发布端口](https://docs.docker.com/engine/network/port-publishing/)、
[Compose up](https://docs.docker.com/reference/cli/docker/compose/up/)、
[Pkcs 安全公告](https://github.com/advisories/GHSA-846f-f764-76mx)、
[Google App Password](https://support.google.com/mail/answer/185833)。
