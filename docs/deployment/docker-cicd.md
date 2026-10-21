# Docker CI/CD 从零设置指南

这份指南用于在**另一台全新 Ubuntu** 上设置 Project1 的自动部署，不是让已经上线的服务器重新安装或重建数据库。

本次部署已由用户验证：两个容器健康，启动迁移、业务流程、邮件及 PDF 可用；
旧容器、专用工具、权限和九个旧目录已清理。这是操作记录，不代表未来服务器的实时状态。

## 流程与操作位置

~~~text
开发、生成 migration → PR → 后端 / 前端 / 迁移测试
                              ↓ 合并 main
                  构建 API / Vue 镜像 → 推送 GHCR
                              ↓
                  GitHub runner 临时加入 Tailscale
                              ↓ 普通 OpenSSH
                  Ubuntu 固定 project1-auto-deploy
                              ↓
                pull 精确 digest → 停 API → 更新容器
                              ↓
                API 应用 pending migrations → 健康检查
~~~

SQL Server 在 Ubuntu 主机，不在 Docker 中；服务器不需要 .NET SDK、EF CLI、Node.js，也不在服务器编译代码。
Tailscale 让 GitHub 连接内网，不会把网站自动变成公网网站。
首次按第 1–14 节准备，只有空库另做第 15 节账号初始化；以后看第 16 节更新、第 17 节排错和第 18 节检查表。

| 标记 | 在哪里操作 |
| --- | --- |
| Windows / PowerShell | 自己的电脑，涉及仓库的命令在 Project1 根目录运行 |
| Ubuntu / Bash | SSH 登录服务器后的终端 |
| SQL | sqlcmd 的 1> 提示符或 SSMS 查询窗口，不是 PowerShell / Bash |
| 网页 | GitHub、Tailscale、Google 设置页面 |

每步报错先停止，不继续粘贴。先替换占位符。sudo 输入 Ubuntu 管理员密码，sqlcmd 输入 SQL 密码，两者不同。

快速导航：

- 准备：[固定配置](#step-1) → [GitHub](#step-2) → [Ubuntu / Docker](#step-3) → [SQL 安装](#step-4)。
- 服务器：[Tailscale 网络](#step-5) → [SSH](#step-6) → [安装文件](#step-7) → [数据库权限](#step-8) → [私密配置](#step-9) → [Nginx](#step-10)。
- 发布：[OIDC / Secrets / Variables](#step-11) → [GHCR](#step-12) → [网络 / 防火墙](#step-13) → [首次部署](#step-14) → [仅空库账号初始化](#step-15)。
- 后续：[日常更新 / migration](#step-16)、[排错](#step-17)、[检查表](#step-18)。

<a id="step-1"></a>

## 1. 准备资料和固定配置

### 环境

- 全新 Ubuntu Server 24.04 LTS，amd64 / x86_64，有人工管理员 sudo 和可信控制台 / LAN SSH。
- 建议至少 4 GB RAM，8 GB 更宽裕；这是同时运行 SQL、API 和系统的实践建议。
- GitHub 仓库管理员权限、Tailscale 管理权限、可发送邮件的 Google 账号。
- 电脑有 Git、OpenSSH、.NET 10 SDK、Node.js；本地 Docker Desktop 不是远程 CD 的必需品。
- 已有业务数据先确认可恢复备份。安装 SQL 不等于搬迁旧数据；不要升级已有 SQL 来照做指南。

| 配置 | 当前代码 / 新环境需记录 |
| --- | --- |
| 仓库 / 分支 | shunbin2310/Project1 / main |
| Ubuntu hostname | homelab-server |
| 人工管理员 | 自己的 Ubuntu 管理员，不是自动部署用户 |
| LAN IP | 例：192.168.1.158；新机器使用自己的地址 |
| Tailscale IP | 安装后 tailscale ip -4，不能照抄旧地址 |
| Linux 自动部署用户 | project1_deploy |
| SQL 登录 / 数据库 | project1_app / Project1Db |
| Compose 项目 / 路径 | project1-auto / /opt/project1-auto |
| 主机入口 | Nginx 80 → 127.0.0.1:8084 |

### 换仓库 / 机器不能只改 Variables

| 改什么 | 必须同步审阅 |
| --- | --- |
| owner / repo | ci.yml 两个 job 条件、镜像路径、source labels；auto-deploy.sh 两个 GHCR 路径；对应测试 |
| hostname | auto-deploy.sh 的 hostname guard；授权 SQL 的 MachineName guard |
| 数据库 / SQL 用户 | Compose 连接与 ExpectedDatabase、授权 SQL、对应测试 |
| Linux 用户 / 路径 / 端口 | sudoers、部署脚本、Compose、Nginx、工作流和测试 |
| Tailscale tags | policy、服务器 tag、OIDC credential、工作流 tags |

新机器可用其他 hostname，但先修改并审阅两个保护条件、测试和提交，再上传对应文件。
不移除保护，不随意重命名旧服务器。重复练习优先用独立 VM / tailnet，不把同一 server tag 任意授予无关服务器。

| 仓库文件 | 用途 |
| --- | --- |
| .github/workflows/ci.yml | 测试、构建、推镜像、部署 |
| compose.ubuntu.yml | 两个应用容器，无数据库容器 |
| .env.docker.ubuntu.example | 六项私密配置的空模板 |
| scripts/deploy/auto-deploy.sh | root 管理的固定入口 |
| scripts/deploy/project1-auto-deploy.sudoers | 限定 sudo 权限 |
| scripts/docker/Prepare-UbuntuStartupMigrations.sql | 管理员一次性准备 SQL 权限 |

<a id="step-2"></a>

## 2. GitHub 仓库与 Actions

**网页：**

1. 使用 Project1，或建立自己的仓库并完成第 1 节适配；默认分支 main。
2. 确保 workflow、Dockerfiles、Compose、所有已执行的 migrations 和 Snapshot 已提交。
3. Settings → Actions → General：允许工作流及其引用的 actions；组织限制、额度或工作流禁用先处理原因。
4. Settings → Secrets and variables → Actions → Variables：
   创建 DOCKER_DEPLOY_ENABLED，值为小写 false，准备结束前不开启。
5. 功能分支开 PR，确认两个测试 job 通过。
6. Settings → Rules → Rulesets，或 Branches：保护 main、要求 PR 和实际 CI 检查。
   选择成功运行显示的 Backend tests and migrations、Frontend checks and production build，以完整显示名称为准。

不要继续要求已删除的 Backend tests and Linux publish，否则一直 Expected；
也不要要求 PR 的 image / deploy 成功，它们在 PR 上正常跳过。

| 事件 | 测试 | 构建 / 推镜像 | 更新 Ubuntu |
| --- | --- | --- | --- |
| PR 到 main | 是 | 否 | 否 |
| push / 合并到 main | 是 | 是 | 仅开关为 true |
| Run workflow（workflow_dispatch） | 是 | 否 | 否 |
| 功能分支 push、没有 PR | 当前无此触发器 | 否 | 否 |

CI 的临时 SQL 不连接 Ubuntu。GITHUB_TOKEN 自动提供，不创建同名 Secret。
镜像 job 已有 packages: write，部署 job 已有 id-token: write，不需要给所有 job 全局写权限。

<a id="step-3"></a>

## 3. Ubuntu 基础软件与 Docker

**Ubuntu / Bash，新机器：**

~~~bash
hostname
uname -m
cat /etc/os-release
timedatectl status
sudo apt-get update
sudo apt-get install -y ca-certificates curl gnupg openssl openssh-server nginx nano sudo util-linux ufw
sudo systemctl enable --now ssh
~~~

确认 Ubuntu 24.04、x86_64、目标机器和时间同步。
按 [Docker 官方 Ubuntu 安装](https://docs.docker.com/engine/install/ubuntu/) 准备 Compose plugin：

~~~bash
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
sudo tee /etc/apt/sources.list.d/docker.sources >/dev/null <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: noble
Components: stable
Architectures: amd64
Signed-By: /etc/apt/keyrings/docker.asc
EOF
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
sudo docker version
sudo docker compose version
sudo docker run --rm hello-world
~~~

已有 Docker / 其他业务容器时先评估冲突包，不照着卸载重装；本文不把部署用户加入 docker 组。

<a id="step-4"></a>

## 4. 安装主机 SQL Server

**仅新机器、尚未安装 SQL 时。** 本例 SQL Server 2025；Ubuntu 24.04 支持从 CU1 起。
版本、包源和许可先核对 [Microsoft 安装说明](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/quickstart-install-ubuntu?view=sql-server-ver17)。

**Ubuntu / Bash：**

~~~bash
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc |
  sudo gpg --batch --yes --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/mssql-server-2025.list |
  sudo tee /etc/apt/sources.list.d/mssql-server-2025.list >/dev/null
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/prod.list |
  sudo tee /etc/apt/sources.list.d/mssql-release.list >/dev/null
sudo apt-get update
sudo apt-get install -y mssql-server
sudo /opt/mssql/bin/mssql-conf setup
sudo apt-get install -y mssql-tools18 unixodbc-dev
sudo systemctl enable --now mssql-server
systemctl status mssql-server --no-pager
~~~

交互选择适用 edition、接受许可、私下设置 sa 密码。练习可评估 Developer / Express；
正式用途要适用许可，不把 Developer 当生产许可。tools 按提示接受许可。不在 -P 参数、聊天或 Git 放真实密码。

容器要通过主机网关连接 SQL，不能只监听 127.0.0.1。新装环境可设置：

~~~bash
sudo /opt/mssql/bin/mssql-conf set network.ipaddress 0.0.0.0
sudo /opt/mssql/bin/mssql-conf set network.tcpport 1433
sudo systemctl restart mssql-server
sudo ss -lntp 'sport = :1433'
sudo /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -C -b \
  -Q 'SELECT @@SERVERNAME AS ServerName, @@VERSION AS Version;'
~~~

输入交互 SQL 密码；-C 信任证书，是私有练习的取舍，不是严格证书验证。
监听所有 IPv4 后必须按第 13 节限制防火墙，云安全组也不开放公网 1433。
已有 SQL 的监听修改、重启另行评估，见 [mssql-conf](https://learn.microsoft.com/en-us/sql/linux/configure/mssql-conf?view=sql-server-ver17)。
后续建立并验证独立 SQL 管理员、按规范管理 sa；不在未验证替代登录前禁用唯一管理员。

<a id="step-5"></a>

## 5. Tailscale 网络与规则

### 网页：tailnet、tags、访问范围

登录 [Tailscale 管理台](https://console.tailscale.com/)，进入 Access controls。
以下仅为**全新专用 tailnet** 示例，先替换邮箱并用页面校验；
已有 tailnet 要合并规则、保留管理通道，不覆盖公司的整份策略。

~~~json
{
  "groups": {
    "group:project1-admins": ["YOUR_TAILSCALE_LOGIN@example.com"]
  },
  "tagOwners": {
    "tag:project1-server": ["group:project1-admins"],
    "tag:github-deploy": ["group:project1-admins"]
  },
  "grants": [
    {
      "src": ["tag:github-deploy"],
      "dst": ["tag:project1-server"],
      "ip": ["tcp:22"]
    },
    {
      "src": ["group:project1-admins"],
      "dst": ["tag:project1-server"],
      "ip": ["tcp:22", "tcp:80"]
    }
  ]
}
~~~

CI 只访问目标 SSH，不访问 SQL。管理员可访问 SSH / HTTP。
规则叠加授权：保留默认 * → * 时，添加窄规则不会限制 CI；检查其他宽规则，不给 CI 管理员 tag。
参见 [tagOwners](https://tailscale.com/docs/reference/syntax/policy-file#tag-owners)、
[Grants](https://tailscale.com/docs/reference/syntax/grants)。

### Ubuntu / Bash：入网

使用 [官方稳定包源](https://pkgs.tailscale.com/stable/#ubuntu-noble)：

~~~bash
curl -fsSL https://pkgs.tailscale.com/stable/ubuntu/noble.noarmor.gpg |
  sudo tee /usr/share/keyrings/tailscale-archive-keyring.gpg >/dev/null
curl -fsSL https://pkgs.tailscale.com/stable/ubuntu/noble.tailscale-keyring.list |
  sudo tee /etc/apt/sources.list.d/tailscale.list >/dev/null
sudo apt-get update
sudo apt-get install -y tailscale
sudo systemctl enable --now tailscaled
sudo tailscale up --advertise-tags=tag:project1-server
tailscale ip -4
tailscale status
~~~

用有权授予该 tag 的管理员打开登录链接。Machines 预期 Connected、tag:project1-server；
启用设备审批时还要审批服务器。Windows 用 100.x 地址也需入同一 tailnet，LAN 管理可继续用 LAN 地址。
本方案用 Tailscale 网络上的**普通 OpenSSH**，不要加 --ssh 改成 Tailscale SSH。

<a id="step-6"></a>

## 6. 部署账号、SSH 密钥与主机校验

### Ubuntu / Bash：专用账号

先检查：

~~~bash
id project1_deploy
~~~

不存在才执行；已有账号只审阅权限，不重建：

~~~bash
sudo adduser --disabled-password --gecos '' project1_deploy
id project1_deploy
~~~

用户不应在 sudo / docker 组，不需要 root SSH，第 7 节仅给固定入口权限。

### Windows / PowerShell：专用 CI 密钥

~~~powershell
$Key = Join-Path $HOME '.ssh\project1_actions'
if (Test-Path $Key) { throw '密钥已存在，先确认是否复用，不覆盖。' }
ssh-keygen -t ed25519 -f $Key -C 'project1-github-actions'
~~~

这把专用密钥的 passphrase 留空（连续回车），现有工作流不解锁加密私钥；
不使用个人主密钥，严格保管和限制对应账号。无扩展名文件是私钥，.pub 是公钥。

第一次 LAN SSH 前，在服务器控制台 / 既有可信会话核对主机指纹：

**Ubuntu / Bash：**

~~~bash
sudo ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
~~~

**Windows / PowerShell：** 替换值，首次提示时核对指纹再接受。

~~~powershell
$Server = 'ADMIN_USER@SERVER_LAN_IP'
ssh $Server
~~~

退出回到 Windows 后上传公钥，后续沿用同一会话的 $Server：

~~~powershell
$Destination = $Server + ':project1_actions.pub'
scp -o StrictHostKeyChecking=yes "$HOME\.ssh\project1_actions.pub" $Destination
if ($LASTEXITCODE -ne 0) { throw '上传公钥失败。' }
~~~

**Ubuntu / Bash：** 最后一条成功（文件不存在）才继续；已有 authorized_keys 不覆盖，先检查。

~~~bash
sudo install -d -m 0700 -o project1_deploy -g project1_deploy /home/project1_deploy/.ssh
sudo test ! -e /home/project1_deploy/.ssh/authorized_keys
~~~

新账号执行：

~~~bash
sudo install -m 0600 -o project1_deploy -g project1_deploy \
  "$HOME/project1_actions.pub" /home/project1_deploy/.ssh/authorized_keys
sudo nano /home/project1_deploy/.ssh/authorized_keys
~~~

公钥行前加 restrict 和一个空格，成为“restrict ssh-ed25519 AAAA... 注释”，保持一整行。
它限制转发 / PTY；工作流用 ssh -T 不需要交互终端。私钥只私下填 GitHub，不上传 Ubuntu、不贴聊天、不提交 Git。

### 取得 GitHub 的可信 known_hosts

在**可信 Ubuntu 会话**执行，显示的是主机公钥，不是私钥：

~~~bash
printf '%s ' "$(tailscale ip -4)"
sudo cut -d ' ' -f 1,2 /etc/ssh/ssh_host_ed25519_key.pub
~~~

保存整行，格式：

~~~text
100.x.x.x ssh-ed25519 SERVER_HOST_PUBLIC_KEY
~~~

这是 DEPLOY_SSH_KNOWN_HOSTS，不是 SHA256 指纹、LAN 地址那一行或用户 authorized_keys。
主机重装后密钥变化必须重新核对，不通过关闭 StrictHostKeyChecking 解决。

<a id="step-7"></a>

## 7. 上传并安装部署文件

### 上传五份公开文件

**Ubuntu / Bash，人工管理员：**

~~~bash
mkdir -m 0700 "$HOME/project1-auto-setup"
~~~

目录已存在先检查，不为重跑先删除。

**Windows / PowerShell，仓库根目录：** 确认文件已审阅、与目标 main 一致。

~~~powershell
git status --short
$Files = @(
    '.\compose.ubuntu.yml'
    '.\.env.docker.ubuntu.example'
    '.\scripts\deploy\auto-deploy.sh'
    '.\scripts\deploy\project1-auto-deploy.sudoers'
    '.\scripts\docker\Prepare-UbuntuStartupMigrations.sql'
)
$Destination = $Server + ':project1-auto-setup/'
scp -o StrictHostKeyChecking=yes @Files $Destination
if ($LASTEXITCODE -ne 0) { throw '上传失败，停止。' }
~~~

不上传本机 .env.docker.host-sql.local 或其他已填密码文件。
Git 已约定这些部署文件用 LF；遇到 CRLF 问题，修正源文件再上传。

### Ubuntu / Bash：root 管理的安装

仅适用于目标尚不存在的首次安装，不是覆盖脚本：

~~~bash
sudo /bin/bash <<'SETUP'
set -euo pipefail
Source="$(getent passwd "$SUDO_USER" | cut -d: -f6)/project1-auto-setup"
App=/opt/project1-auto
Command=/usr/local/sbin/project1-auto-deploy
Rule=/etc/sudoers.d/project1-auto-deploy
for p in "$App" "$Command" "$Rule"; do
  if [ -e "$p" ] || [ -L "$p" ]; then
    printf 'STOP：目标已存在，不覆盖：%s\n' "$p"
    exit 1
  fi
done
for p in /opt /usr/local/sbin /etc/sudoers.d; do
  [ "$(readlink -f "$p")" = "$p" ]
  [ "$(stat -c '%U:%G:%a' "$p")" = root:root:755 ]
done
bash -n "$Source/auto-deploy.sh"
visudo -cf "$Source/project1-auto-deploy.sudoers"
install -d -m 0700 -o root -g root "$App"
install -m 0644 -o root -g root "$Source/compose.ubuntu.yml" "$App/compose.yml"
install -m 0600 -o root -g root "$Source/.env.docker.ubuntu.example" "$App/.env"
install -m 0600 -o root -g root "$Source/Prepare-UbuntuStartupMigrations.sql" "$App/Prepare-UbuntuStartupMigrations.sql"
install -m 0755 -o root -g root "$Source/auto-deploy.sh" "$Command"
install -m 0440 -o root -g root "$Source/project1-auto-deploy.sudoers" "$Rule"
visudo -c
sudo -l -U project1_deploy
SETUP
~~~

预期部署账号只允许 root 执行 project1-auto-deploy，不是任意 Docker / SQL。
入口只接收三个固定格式参数：commit、API digest、frontend digest。
此时尚未启动应用或执行授权 SQL，开关仍 false。

<a id="step-8"></a>

## 8. 数据库与应用账号

| 情况 | 操作 |
| --- | --- |
| 已有业务 Project1Db | 保留数据、用户、迁移历史，不 CREATE / DROP / 重建；升级前确认备份 |
| 新机器搬旧数据 | 先管理员恢复备份、核对表、业务数据和管理员登录；不是 CI 工作 |
| 完全从零练习 | 管理员先建空库，API 建表，第 15 节另做首次账号初始化 |

不要用空库代替应恢复的业务库。跨机器恢复需要核对目标 SQL 版本、备份可读权限、逻辑文件及数据文件路径，
不使用覆盖现有业务库的通用恢复命令；按 [Microsoft 备份 / 恢复迁移说明](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-migrate-restore-database?view=sql-server-ver17) 单独完成。
SQL 语句不粘到 PowerShell / Bash，先打开 sqlcmd：

**Ubuntu / Bash：**

~~~bash
sudo /opt/mssql-tools18/bin/sqlcmd -S tcp:127.0.0.1,1433 -U sa -C -b
~~~

看到 1> 后才输入 SQL，GO 单独一行，:QUIT 退出。
-b 报错可能回到 Bash，注意提示符，不继续把 SQL 当 shell 命令。

### SQL：仅从零练习时创建空库

~~~sql
USE [master];
GO
IF DB_ID(N'Project1Db') IS NOT NULL
    THROW 50000, 'Database already exists; stop and inspect.', 1;
GO
CREATE DATABASE [Project1Db];
GO
~~~

已有库 / 待恢复分支跳过。应用登录没有 CREATE DATABASE 权限。

### SQL：专用应用登录

~~~sql
USE [master];
GO
SELECT name FROM sys.sql_logins WHERE name = N'project1_app';
GO
~~~

仅不存在时才创建。先私下替换密码占位符，不能原样执行，填写后不分享或提交：

~~~sql
CREATE LOGIN [project1_app]
WITH PASSWORD = N'REPLACE_WITH_A_PRIVATE_STRONG_DB_PASSWORD', CHECK_POLICY = ON;
GO
~~~

用长随机密码满足 SQL 策略，与 .env 的 DB_PASSWORD 一致。
可使用长随机大小写字母和数字，避免分号 / 引号等需要连接串转义的字符。
已有账号复用正确密码，不随意重置共用登录或删除数据库。

若恢复到新实例导致 SID 不一致，确认是同一身份后，管理员在 Project1Db 中执行
ALTER USER [project1_app] WITH LOGIN = [project1_app]; 修复映射；不对不明用户直接改。

### SQL：reader / writer / DDL 权限

仍在管理员 sqlcmd 中：

~~~sql
:r /opt/project1-auto/Prepare-UbuntuStartupMigrations.sql
~~~

该文件检查 MachineName、固定库和用户映射，不建库、不执行 EF migration。
db_ddladmin 是广泛数据库 DDL 权限，不是仅 AddColumn，这是启动迁移的明确取舍。
应用不使用 sa / sysadmin / db_owner。

~~~sql
:QUIT
~~~

**Ubuntu / Bash：** 用应用登录和相同密码验证：

~~~bash
sudo /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U project1_app -d Project1Db -C -b \
  -Q "SELECT DB_NAME() AS DatabaseName, SUSER_SNAME() AS LoginName,
      IS_ROLEMEMBER(N'db_datareader') AS CanRead,
      IS_ROLEMEMBER(N'db_datawriter') AS CanWrite,
      IS_ROLEMEMBER(N'db_ddladmin') AS CanMigrate;"
~~~

预期 Project1Db 和三个 1。这里只验证主机登录；容器网络还需第 13 节。

<a id="step-9"></a>

## 9. 私密配置与 Gmail

**Ubuntu / Bash，人工管理员私下编辑：**

~~~bash
openssl rand -hex 32
sudo nano /opt/project1-auto/.env
~~~

随机值可用作 JWT_SIGNING_KEY，不是 SQL 密码或 Google 应用密码；不分享输出 / 截图。
只填六项：

| 名称 | 值 |
| --- | --- |
| DB_USER | project1_app |
| DB_PASSWORD | 第 8 节实际 SQL 应用登录密码 |
| JWT_SIGNING_KEY | 新生成、至少 32 字符的随机密钥 |
| NEW_USER_PASSWORD | 在应用创建新用户时使用的私密默认密码，符合应用密码规则 |
| GMAIL_ADDRESS | 实际发件 Gmail 地址 |
| GMAIL_APP_PASSWORD | Google 应用密码，不是普通 Google 登录密码 |

.env 是 Compose dotenv，不是 Bash 脚本，不要 source。
含 $、# 等特殊字符时按 dotenv 规则用单引号包住完整值，不随意改变密码内容。
参见 [Compose dotenv 引号规则](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/)。
Google 显示密码的分组空格不作为内容，保存连续字符。
NEW_USER_PASSWORD 不重置已有用户密码；默认 DemoUsers__Enabled=false 也不会创建 demo 用户。

**网页：Google Account → Security → 2-Step Verification → App passwords。**

先开启两步验证，再生成专用应用密码。组织策略 / 账号限制可能不提供入口，不关闭安全措施绕过。
参见 [Google 应用密码](https://support.google.com/mail/answer/185833)。
服务器模板固定 Gmail SMTP 587 / TLS，发件地址和登录地址一致；换服务商须审阅 Compose。

只检查权限，不打印文件：

~~~bash
sudo stat -c '%U:%G %a %n' /opt/project1-auto /opt/project1-auto/.env
~~~

预期 root:root 700 / 600。不要分享完整 docker inspect、没加 --quiet 的 Compose config、
.env、私钥或 Docker 登录配置，可能含明文凭据。

<a id="step-10"></a>

## 10. 主机 Nginx

**Ubuntu / Bash，新机器：** 第 3 节已安装；参考 [Ubuntu Nginx](https://ubuntu.com/server/docs/how-to/web-services/install-nginx/)。

确认以下 project1 文件和链接没有现有业务；不覆盖已有站点。新机器编辑：

~~~bash
sudo nano /etc/nginx/sites-available/project1
~~~

内容如下，网站所有路径转给容器，不需要 /var/www/project1：

~~~nginx
server {
    listen 80 default_server;
    server_name _;
    server_tokens off;

    location / {
        proxy_pass http://127.0.0.1:8084;
        proxy_http_version 1.1;
        proxy_set_header Host $http_host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_connect_timeout 5s;
        proxy_read_timeout 60s;
    }
}
~~~

若发行版默认站点链接存在，先检查：

~~~bash
sudo ls -l /etc/nginx/sites-enabled/default
sudo readlink -f /etc/nginx/sites-enabled/default
~~~

仅确认是全新机器、链接指向 /etc/nginx/sites-available/default 时移除该链接：

~~~bash
sudo unlink /etc/nginx/sites-enabled/default
~~~

链接本来不存在则跳过 unlink。然后启用新站点：

~~~bash
sudo ln -s /etc/nginx/sites-available/project1 /etc/nginx/sites-enabled/project1
sudo nginx -t
sudo systemctl enable --now nginx
sudo systemctl reload nginx
~~~

应用未启动前访问返回 502 是此阶段预期。
本文是受控 LAN / 私有练习 HTTP；面向公网前须另做域名、HTTPS、访问控制和备份，
不能把 CI/CD 成功当公网安全验收。

<a id="step-11"></a>

## 11. Tailscale OIDC 与 GitHub Secrets / Variables

### 网页：GitHub main/push 的 OIDC 信任

Tailscale → Settings → [Trust credentials](https://console.tailscale.com/admin/settings/trust-credentials)
→ Credential → OpenID Connect：

| 项目 | 填写 |
| --- | --- |
| Description | Project1 Docker auto deployment from main；只是备注，可改 |
| Issuer / URL | GitHub / https://token.actions.githubusercontent.com |
| Subject | 自己仓库 main 分支的准确 sub，见下文 |
| Custom claim | event_name = push |
| Custom claim（建议） | workflow_ref = shunbin2310/Project1/.github/workflows/ci.yml@refs/heads/main，换仓库同步替换 |
| Scopes | Auth Keys / auth_keys 写入权限，不选全部管理权限 |
| Tags | 仅 tag:github-deploy，不是 server tag |

保存后复制同一个 credential 的 Client ID 和 Audience。
本方案是 OIDC，不另建长期 OAuth secret，也不需要 TS_OAUTH_SECRET。
虽然工作流参数名是 oauth-client-id，也接收 federated identity 的 Client ID。
参考 [Tailscale OIDC](https://tailscale.com/docs/features/workload-identity-federation)
与 [GitHub Action](https://tailscale.com/docs/integrations/github/github-action)。

旧手动部署的 event_name=workflow_dispatch 不能原样留下；自动部署是 push。
不把 Subject 放宽成任意仓库 / 任意分支。

### Subject 两种默认格式

普通名称格式：

~~~text
repo:OWNER/REPO:ref:refs/heads/main
~~~

包含不可变 ID 的格式：

~~~text
repo:OWNER@OWNER_ID/REPO@REPO_ID:ref:refs/heads/main
~~~

本次 Project1 已验证使用：

~~~text
repo:shunbin2310@138860553/Project1@1334916963:ref:refs/heads/main
~~~

ID 是公开标识，不是密码，只适用于这个仓库，不能照搬给新仓库 / fork。
从 GitHub 的 OIDC 设置核对格式；安装并登录 gh 后可查询元数据：

**Windows / PowerShell，可选查询，替换 OWNER/REPO：**

~~~powershell
gh api repos/OWNER/REPO --jq '{owner_id: .owner.id, repository_id: .id}'
~~~

查询 ID 不代表仓库没用自定义 Subject。Environment、自定义模板、转移 / 重命名也可能改变 sub。
保留与实际仓库匹配的完整格式，不盲目在新旧格式之间切换。
不匹配时看 credential 的 token exchange 错误，不输出完整 OIDC JWT。
参考 [GitHub OIDC Subject](https://docs.github.com/en/actions/reference/security/oidc#immutable-subject-claims)。

### GitHub 四个 Secrets

**网页：仓库 → Settings → Secrets and variables → Actions → Secrets → New repository secret。**

| Secret 名称 | 值 / 来源 |
| --- | --- |
| TS_OAUTH_CLIENT_ID | 上述 Tailscale OIDC Client ID |
| TS_AUDIENCE | 同一个 credential 的 Audience，复制，不自行拼接 |
| DEPLOY_SSH_PRIVATE_KEY | 第 6 节无扩展名私钥的完整多行文本，包含 BEGIN / END |
| DEPLOY_SSH_KNOWN_HOSTS | 第 6 节可信会话取得的 Tailscale IP + 主机公钥整行 |

Windows 可用下面命令**私下打开**并复制私钥到 Secret，不打印到聊天 / 日志：

~~~powershell
notepad "$HOME\.ssh\project1_actions"
~~~

Client ID / Audience 本身不是秘密，但当前工作流从这两个 Secret 名称读取，仍按此位置设置。

### GitHub 三个 Variables

切到 Variables → New repository variable：

| 名称 | 值 |
| --- | --- |
| SERVER_TAILSCALE_IP | 本次新服务器 tailscale ip -4 结果 |
| DEPLOY_SSH_USER | project1_deploy |
| DOCKER_DEPLOY_ENABLED | 现在保持 false，到第 14 节才设 true |

Variables 明文显示正常，不放 SQL / Gmail / JWT 密码。
本流程不要求 GitHub Environment；以后若添加，需同步核对 OIDC Subject。
参考 [GitHub Secrets](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets)
和 [Variables](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-variables)。

<a id="step-12"></a>

## 12. 首次构建镜像与 GHCR 登录

### 开关关闭时先发布镜像

合并已通过 PR 测试的代码到 main，预期：

1. Backend tests and migrations：Success。
2. Frontend checks and production build：Success。
3. Build and publish API and Vue images：Success。
4. Automatically update Ubuntu：Skipped，因为开关仍 false。

这是准备成功，不是已部署。记录 run 的**完整 40 位 commit SHA**。
个人 / 组织 Packages 应出现 project1-api、project1-frontend，检查仍是 private、关联正确仓库、
Manage Actions access 给该仓库写入权限；以前手动创建的包可能需要补关联或授权。
CI 用自动 GITHUB_TOKEN 推送，不把服务器读取 PAT 交给 CI。

### 网页：只拉取 GHCR 的 PAT

GitHub 个人 Settings → Developer settings → Personal access tokens → Tokens (classic)。
建立专用、有到期日的 token，只选 read:packages，不额外选 write / delete packages、repo 或管理权限。
账号自身需有包的读取权，组织有 SSO 要按要求授权。
token 只私下输入服务器 Docker 登录，不放应用 .env / GitHub Variables / 镜像。
参见 [GHCR 认证说明](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)。

**Ubuntu / Bash，替换用户名：**

~~~bash
sudo install -d -m 0700 -o root -g root /root/.docker
sudo docker --config /root/.docker login ghcr.io --username YOUR_GITHUB_USERNAME
sudo chmod 0600 /root/.docker/config.json
~~~

Password 提示输入 PAT，不是 GitHub 登录密码，预期 Login Succeeded。
无 credential helper 时会警告 config.json 存储未加密凭据：不是登录失败，但文件必须保密。
更强保护见 [Docker credential store](https://docs.docker.com/reference/cli/docker/login/)；不展示 config.json。

<a id="step-13"></a>

## 13. 配置检查、容器网络与防火墙

### 新机器：拉取候选镜像，只创建、不启动容器

**Ubuntu / Bash：** Commit 换成第 12 节真实 SHA，改仓库时同步改两个镜像路径。
此块只适用于全新 project1-auto，不对运行环境重复执行。
创建实际 Compose 网络，便于精确配置防火墙；**不启动 API、不运行 migration**。

~~~bash
sudo /bin/bash <<'PREPARE'
set -euo pipefail
Commit=REPLACE_WITH_FULL_40_CHARACTER_MAIN_COMMIT
[[ "$Commit" =~ ^[0-9a-f]{40}$ ]]
[[ -z $(docker ps -a --filter label=com.docker.compose.project=project1-auto --format '{{.Names}}') ]]
export API_IMAGE="ghcr.io/shunbin2310/project1-api:$Commit"
export FRONTEND_IMAGE="ghcr.io/shunbin2310/project1-frontend:$Commit"
compose() {
  docker compose --project-name project1-auto \
    --project-directory /opt/project1-auto \
    --env-file /opt/project1-auto/.env -f /opt/project1-auto/compose.yml "$@"
}
compose config --quiet
docker --config /root/.docker pull "$API_IMAGE"
docker --config /root/.docker pull "$FRONTEND_IMAGE"
compose create --no-build --pull never
docker network inspect project1-auto_default --format '{{range .IPAM.Config}}{{println .Subnet}}{{end}}'
PREPARE
~~~

预期容器 Created、输出实际 bridge subnet。
不要把初次 pull 的 digest 固定给所有后续版本；真实 CD 用本次构建返回的 digest。
create 不同于 up，见 [Compose create](https://docs.docker.com/reference/cli/docker/compose/create/)。

### 防火墙限制实际来源

需要：人工管理 SSH、Tailscale SSH 网络流量、受控 HTTP、实际 Docker bridge 到主机 SQL。
Compose 前端只发布 127.0.0.1:8084，API 无主机端口。
不要为连 SQL 就向公网或整个 tailnet 放开 1433。

**Ubuntu / Bash：** 三个占位符先替换。保留控制台 / 管理会话，先放行自己的 SSH，防止锁住自己。
已有防火墙先评估规则，不覆盖重配。

~~~bash
sudo ufw status verbose
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow from ADMIN_PC_LAN_IP to any port 22 proto tcp
sudo ufw allow from TRUSTED_LAN_CIDR to any port 80 proto tcp
sudo ufw allow in on tailscale0 to any port 22 proto tcp
sudo ufw allow in on tailscale0 to any port 80 proto tcp
sudo ufw allow from ACTUAL_COMPOSE_BRIDGE_CIDR to any port 1433 proto tcp
sudo ufw enable
sudo ufw status numbered
~~~

例：管理 PC 是单个 192.168.1.x，LAN 可能是 192.168.1.0/24；
bridge 必须来自 inspect，不猜 172.17.0.0/16、不放开所有 172 网段。
tailnet policy 仍只给 CI 22，只有管理员有 HTTP。
既有宽 allow 不会被窄规则自动抵消；云安全组 / 路由器不向公网开放 1433 / 8084，也无需对全网开放 SSH。

Docker 发布端口可能绕过部分 UFW 路径，不能只靠 deny 认定容器安全；保持 loopback 绑定并核对实际规则。
参见 [Ubuntu UFW](https://ubuntu.com/server/docs/how-to/security/firewalls/)、
[Docker 防火墙](https://docs.docker.com/engine/network/packet-filtering-firewalls/)。

### Windows / PowerShell：部署前测试固定 SSH

Windows 已加入同一 tailnet 后，把第 6 节可信主机公钥整行私下保存到下面的 KnownHosts 文件。
它不是普通密码，但必须保证来源可信。首次文件不存在时用编辑器新建即可。

~~~powershell
$TailscaleIp = 'REPLACE_WITH_SERVER_TAILSCALE_IP'
$KnownHosts = Join-Path $HOME '.ssh\project1_known_hosts'
notepad $KnownHosts
~~~

保存后执行，只列出部署用户权限，不启动应用：

~~~powershell
ssh -T -i "$HOME\.ssh\project1_actions" -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$KnownHosts" "project1_deploy@$TailscaleIp" 'sudo -n -l'
if ($LASTEXITCODE -ne 0) { throw 'SSH 或权限检查失败，先不要启用部署。' }
~~~

预期只允许固定 project1-auto-deploy。也要确保四个 Secrets 用的是同一把私钥 / 同一份主机公钥。

<a id="step-14"></a>

## 14. 第一次自动部署与业务验收

### 准备检查

- 应用 SQL 登录验证通过，bridge 到主机 1433 可达；Nginx 语法通过，80 指向 8084。
- 四 Secrets、三 Variables、OIDC main / push、固定 host key 正确。
- 空库先读第 15 节，决定是否做受控 demo 初始化。
- 若替换旧系统：先确认备份可恢复、暂停业务写入，人工停止同库旧 API / worker。
  新机器没有旧服务，不需要任何旧站清理。

脚本只自动拒绝旧 Compose 项目 project1-ubuntu-production 还运行；
不会发现所有原生服务 / 其他同库 writer，也不替你停它们。

### 网页：开启并触发部署

把 DOCKER_DEPLOY_ENABLED 改为小写 true，选一种方式：

1. 对**当前仍是 main 最新 SHA**、原事件 push、测试与镜像成功但 deploy 跳过的 run，
   点击 Re-run all jobs。改变量本身不触发 run；重跑保留原 push 的 SHA / ref，不是 workflow_dispatch。
   参见 [GitHub 重新运行](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)。
2. 通过正常 PR 合并一个已审阅的新提交到 main，触发新 push 流程。

不要用 Run workflow 按钮代替：当前手动触发只测试。
不用反复造空提交；旧 SHA 已非 main 最新时，部署保护会拒绝，选择最新 run。
预期四 job 全部 Success，最后出现 Deployed <SHA>。

### Ubuntu / Bash：技术验证

~~~bash
sudo docker ps \
  --filter label=com.docker.compose.project=project1-auto \
  --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}'
curl --noproxy '*' -fsS http://127.0.0.1:8084/api/health
curl --noproxy '*' -fsS http://127.0.0.1/api/health
sudo docker logs project1-auto-api-1 2>&1 |
  grep -E 'Applying .*pending EF migrations|Startup EF migration completed|No pending EF migrations'
sudo stat -c '%U:%G %a %n' /opt/project1-auto/release.env
~~~

两个容器 healthy，前端 127.0.0.1:8084->8080，API 无主机发布端口。
首次迁移有 Applying / completed；以后没有 pending 则 skipped。
release.env 是 commit / digest 记录，不是私密 .env。

### 浏览器：业务验收

打开 http://SERVER_LAN_IP/，或有 HTTP 权限的 Tailscale 地址：

- 已有库：原管理员能登录，原商品、订单仍在。
- 创建商品 / 供应商，验证 PR、审批、报价、PO、收货、库存。
- PDF 能打开，商品、数量、单价、金额正确。
- 只发往自己控制的测试收件人，检查收件箱及 Spam，确认附件。
- Sent 不保证进收件箱；Pending / Failed 看详情，避免反复重发。

按 [UI 测试指南](../UI_TEST_GUIDE.md) 做业务步骤，但 Ubuntu 不使用 localhost:5173 / 5165，
也不沿用其本地 Development 公开示例密码。用明确测试标记，测试会产生真实记录 / 邮件。
/api/health 是 liveness，不单独证明数据库、数据保留、业务流程、PDF 或实际邮件送达。

<a id="step-15"></a>

## 15. 仅空库：第一次创建管理员

**已有 / 恢复业务数据库一律跳过。** 默认 DemoUsers__Enabled=false：
会建立角色、模板和 workflow，但不会创建一个能登录的管理员。
当前没有公开注册或专用正式 bootstrap 命令；空库练习需明确做一次初始化。

下面只适用于**确认空库的私有练习**，不用于已有业务库 / 公网正式系统：

1. 第一次部署前，在管理员 SQL 窗口运行下面的只读查询，确认没有业务表；并确认没有待恢复的数据。
   若已有 AspNetUsers 或业务记录，停止，走已有库分支。
2. 确认 .env 的 NEW_USER_PASSWORD 为自己的私密强密码，不是 README 公开示例。
3. Ubuntu 私下编辑 /opt/project1-auto/compose.yml，仅把 DemoUsers__Enabled 从 "false" 临时改为 "true"。
   不修改仓库默认值。
4. 完成第 14 节首次部署。创建 admin@demo.local 等八个 demo 账号和部门，
   新账号密码来自 NEW_USER_PASSWORD；共用密码仅用于这次受控练习。
5. 用 admin@demo.local 登录，在 Users 新建自己的独立管理员，选 ADMIN 角色，
   退出并验证新管理员可登录。当前应用新用户也用 NEW_USER_PASSWORD，
   不是自动分配独立随机密码的正式入职系统。
6. 立即把服务器 compose 的 DemoUsers__Enabled 改回 "false"，按下面命令重建，
   **不能只编辑文件就认为容器已使用 false**。

第 1 步空库检查（必须在首次 API 启动前；空库预期零行）：

~~~sql
USE [Project1Db];
GO
SELECT name FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name;
GO
~~~

第 6 步关闭初始化后，**Ubuntu / Bash，仅首次部署已成功、有 release.env、无故障标记时**：

~~~bash
sudo /bin/bash <<'DISABLE_BOOTSTRAP'
set -euo pipefail
[ -f /opt/project1-auto/release.env ]
[ ! -e /opt/project1-auto/needs-attention ]
. /opt/project1-auto/release.env
ApiDigest=$(printf '%s' "$API_IMAGE" | cut -d '@' -f 2)
FrontendDigest=$(printf '%s' "$FRONTEND_IMAGE" | cut -d '@' -f 2)
/usr/local/sbin/project1-auto-deploy "$COMMIT_SHA" "$ApiDigest" "$FrontendDigest"
DISABLE_BOOTSTRAP
~~~

这里 source 的是 root 管理的非私密版本记录，不是应用 .env。
会短暂停 API；若失败按第 17 节调查，不反复执行。

7. 用独立管理员在 Users 停用不需要的 demo 用户。false **不会删除 / 停用已有 demo 账号**。
   如仍需分角色练习，明确保留范围、私密密码，不对公网开放。
8. 核对 compose 恢复仓库默认 false，重新验收健康和业务。

已有库启用 demo seeder 可能更新 demo 用户部门、角色及启用状态，
不能用于“修复不明正式登录问题”。真正正式新系统需另行设计经审阅的管理员初始化与独立密码流程。

<a id="step-16"></a>

## 16. 以后更新与 migration

普通功能：本地开发 / 测试 → push 功能分支 → PR → CI 通过 / 审阅 → 合并 main。
剩下自动构建、推镜像和部署，不手工 scp 镜像包、不再执行旧 finalize。

### 修改 Entity / 增加列

**Windows / PowerShell，仓库根目录：**

~~~powershell
$env:Database__MigrateOnStartup = 'false'
dotnet tool restore
dotnet tool run dotnet-ef -- migrations add DescribeYourChange --project backend/Project1.Api --startup-project backend/Project1.Api --context AppDbContext
~~~

生成源码，不更新数据库。审阅 Up / Down、Designer、Snapshot，与 Entity 一起提交。
CI 检查缺失 migration 的模型变更，在临时 SQL 测试；
部署时 API 内的 MigrateAsync 应用 pending migrations，服务器不运行 dotnet ef。
已执行的 migration / 迁移历史不能为了“清理”删除。

新增 nullable 列通常保留旧行，删列、改类型、收紧长度 / 约束可能丢失数据或失败；
按风险准备可恢复备份。Down 加回列不会恢复原值，旧容器镜像也不是数据库备份。
见 [启动迁移说明](api-startup-migrations.md)、[CI 迁移测试](../CI_MIGRATION_TESTS.md)。

### 基础配置更新不是镜像更新

当前 CI 只传镜像 digest，**不会自动覆盖**服务器 compose、入口脚本、Nginx、sudoers 或 .env。
修改基础配置须管理员单独审阅、备份、安装和验证，保证服务器与目标代码一致。
生产密钥始终留在服务器，不通过 Git 推送。

<a id="step-17"></a>

## 17. 排错与恢复边界

| 现象 | 检查 |
| --- | --- |
| PR 的 images / deploy Skipped | 正常，PR 只测试 |
| main images 成功但 deploy Skipped | 开关是否小写 true、仓库条件是否适配 |
| Required check 一直 Expected | 分支保护是否还要求旧 job 名称 |
| OIDC token exchange 失败 | Subject、ID、main ref、event_name=push、workflow_ref、Audience、auth_keys、tag，查看 credential 错误 |
| Tailscale Connected 但 SSH timeout | IP、grant、server tag、sshd、UFW、云规则；不用 Tailscale SSH |
| Host key verification failed | Secret 是否是正确 Tailscale IP + 可信主机公钥；不关闭校验 |
| Permission denied (publickey) | 用户、公私钥对应、authorized_keys / home 权限、私钥是否有 passphrase |
| sudo 不允许 / 要密码 | 固定路径、0440 sudoers、visudo -c、sudo -l -U project1_deploy |
| GHCR denied / unauthorized | 包存在、账号读取权、PAT 到期 / SSO、是否登录 root 的 /root/.docker |
| 入口立即退出 | hostname、root 目录权限、参数、同库旧 writer、needs-attention，不移除保护绕过 |
| 旧 Production Compose 不匹配错误 | 不再用已退役 project1-docker-update，当前入口是 project1-auto-deploy |
| API 连不上 SQL | 监听、密码 / 库名 / SID、host-gateway、实际 bridge 到 1433 的防火墙 |
| migration 失败 | 受控日志、实际 schema / 迁移历史、DDL 权限；保留现场 |
| healthy 却无人能登录 | 空库是否没初始化；已有库先查连接目标和账号，不开 demo seed 碰运气 |
| Gmail Sent 但没收到 | 收件地址、Spam、发件账号、附件；避免重复发送 |
| StartedAt 比系统时间晚 | 核对 date -u、timedatectl 和容器时间，不仅凭 Up 判断重启循环 |

**Ubuntu / Bash，只读排查：**

~~~bash
sudo docker ps -a --filter label=com.docker.compose.project=project1-auto
sudo docker logs --tail 150 project1-auto-api-1
sudo docker logs --tail 100 project1-auto-frontend-1
sudo test -e /opt/project1-auto/needs-attention && printf '部署故障标记存在\n'
systemctl status mssql-server --no-pager
sudo nginx -t
~~~

日志可能含业务内容，分享前脱敏；不贴完整 Env、.env、私钥或 PAT。

- pull 失败：脚本尚未停止应用，先修复拉取原因。
- pull 后 / migration 开始后失败：schema 可能已变化；脚本尝试停候选 API，并保留 needs-attention。
- 不直接删标记重跑、不盲目启动旧镜像、不自动 Down / restore。
- 管理员核对原因、schema、数据和迁移历史，审阅修复方案后显式恢复。
- 开关改 false 只阻止未来部署 job，不取消已运行的远程部署，也不停当前网站。

release.env 为最后成功版本，previous-release.env 为前次成功版本，不是 DB 备份。
脚本加锁串行并拒绝 main 旧 SHA；通过健康检查后才把 API restart 改为 unless-stopped。
断电 / 故障留下标记先调查，“再跑一次”不是通用恢复方案。

<a id="step-18"></a>

## 18. 完成检查表与日常维护

- [ ] 两个镜像在正确 GHCR private 包，读取权已验证。
- [ ] 四 Secrets、三 Variables、仓库条件与镜像路径一致。
- [ ] Tailscale server Connected、tag 正确，CI 仅目标 TCP 22，无额外 allow-all。
- [ ] SSH 固定主机公钥，CI 私钥专用；账号无 docker 组、无任意 sudo。
- [ ] root 管理 compose / script / .env，私密值未提交。
- [ ] 正确 Project1Db，应用 reader / writer / ddladmin，非 sa / db_owner。
- [ ] SQL 仅必要私有来源，8084 仅 loopback，API 无主机端口。
- [ ] 四 job 成功、两容器 healthy、80 / 8084 health 通过。
- [ ] 管理员登录、数据保留、商品 / workflow / PDF / 邮件实际验收。
- [ ] 空库初始化已关并实际重建，遗留 demo 用户已明确处置。
- [ ] 正式业务有独立备份与恢复验证；公网 HTTPS 另行完成。

关注 PAT 到期、SSH / Google 密钥轮换、磁盘日志、主机 / 容器更新、SQL 备份和恢复演练。
镜像按 commit 标记、按 digest 部署，不依赖 latest。
不运行 docker system prune --volumes、不删除 /var/opt/mssql 来清理旧部署。
新环境不需要旧 ZIP/TAR、hash 安装器、migration-prepare、finalize；
旧文件清理是独立一次性工作，不是每次发布步骤。

本文基于当前仓库实现与 2026-10-20 核对的官方说明。界面、包版本及组织策略变化时，
按各节官方链接复核。不要把整份指南当可重复执行的安装脚本。
