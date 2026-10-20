# Docker 自动部署：从开发完成到 Ubuntu

## 唯一流程

PR → 测试；main push → 测试 → 构建 API/Vue 两个镜像 → 推送 GHCR →
Tailscale/SSH → Ubuntu pull 精确 digest → Compose 更新 → API 启动迁移 → 健康检查。

只有一个工作流 .github/workflows/ci.yml。不再使用 ZIP/TAR 传输、发布 manifest、
手动 migration SQL 发布流程、切换状态机或逐文件 hash 安装器。

当前只实现仓库配置，尚未推送、安装服务器配置或运行新自动发布。
Ubuntu 现有网站、SQL Server 和业务数据未动。

## 仓库里的入口

| 文件 | 用途 |
|---|---|
| .github/workflows/ci.yml | 测试、构建、推镜像、自动部署 |
| compose.ubuntu.yml | 正式环境的两个容器；不包含数据库容器 |
| .env.docker.ubuntu.example | 服务器私密配置的空白参考 |
| scripts/deploy/auto-deploy.sh | 服务器 pull / stop API / up / 健康检查 |
| scripts/deploy/project1-auto-deploy.sudoers | 部署账号只允许运行这个固定入口 |

API、Vue 的 Dockerfile 继续使用现有实现。镜像不包含 SQL/Gmail/JWT/SSH 密钥。

## 首次准备：只做一次，未执行

1. 在 GHCR 创建/发布两个包后，确认它们保持 private，允许此仓库的 Actions 写入。
   镜像名为 ghcr.io/shunbin2310/project1-api 和 project1-frontend。
   CI 使用自动提供的 GITHUB_TOKEN；服务器只需要 read:packages 的拉取凭据，
   在服务器私下登录 GHCR，不把 token 放进应用 .env、镜像或 SSH 参数。

2. 管理员创建 root-owned /opt/project1-auto（建议 0700），安装：
   - compose.ubuntu.yml → /opt/project1-auto/compose.yml，root:root 0644
   - 参考模板 → /opt/project1-auto/.env，root:root 0600，本机私下填写
   - auto-deploy.sh → /usr/local/sbin/project1-auto-deploy，root:root 0755
   - sudoers 模板 → /etc/sudoers.d/project1-auto-deploy，root:root 0440，先 visudo 校验
   不给部署账号 Docker 组、任意 sudo 或 root SSH 权限。脚本不接受路径和 shell 命令。

3. .env 填 DB_USER、DB_PASSWORD、JWT_SIGNING_KEY、NEW_USER_PASSWORD、
   GMAIL_ADDRESS、GMAIL_APP_PASSWORD。数据库固定为现有 Project1Db。
   新建或复用专用应用 login 并映射到这个库；绝不能为了换密码删除/重建库。
   默认权限脚本针对新账号 project1_app，先私下创建 login 再审阅授权脚本；
   如果选用其他账号，需管理员明确审阅对应授权，不能直接照搬。
   不重置现有业务用户密码，DemoUsers__Enabled=false。不要使用 Windows 的私密文件。
   启动迁移所需数据库级 DDL 权限见 [迁移说明](api-startup-migrations.md)。

4. 配置 GitHub Repository Variables：
   - SERVER_TAILSCALE_IP：当前服务器的 Tailscale IPv4
   - DEPLOY_SSH_USER：默认 project1_deploy
   - DOCKER_DEPLOY_ENABLED：准备完成之前保持 false；完成后设为 true

   Repository Secrets：
   - TS_OAUTH_CLIENT_ID、TS_AUDIENCE
   - DEPLOY_SSH_PRIVATE_KEY、DEPLOY_SSH_KNOWN_HOSTS

   SSH 主机密钥必须通过现有可信会话核对，不能用关闭校验来解决连接错误。
   旧 Tailscale OIDC 信任若仅允许 workflow_dispatch，
   需要允许此仓库 main/push 的 ci.yml，网络授权只开放到该服务器 SSH。
   不直接沿用旧“手动发布”身份条件，也不扩大为任意仓库/分支。

5. 第一次切换：先保留旧站，准备并验证新镜像、配置和数据库权限。
   真正替换时暂停用户/外部写入，手动停止旧 project1-ubuntu-production 容器，
   再启动新 project1-auto；期间短暂停机。脚本发现旧容器仍在运行会直接拒绝，
   不允许两个版本在同一个库上并行迁移/写入。
   原主机 Nginx 继续把端口 80 转到 127.0.0.1:8084，所以网址不变。
   具体初始化/切换命令等实际服务器检查后逐步提供，不让 CI 自动删除旧站。

不开 DOCKER_DEPLOY_ENABLED 时，main 仍测试和构建镜像，但不连接 Ubuntu。
PR、普通功能分支及手动 CI 检查不推镜像、不部署。不要提前删除服务器旧目录。

## 之后每次更新

正常开发 → PR 审阅/测试 → 合并 main，剩下自动执行。
每次用完整 commit 标记镜像，实际部署用这次构建返回的 sha256 digest，不依赖 latest。
工作流串行且不自动取消正在发布的任务；发布前拒绝已经不是 main 最新提交的旧 run。

服务器先校验配置、pull 镜像。拉取失败时不停止现有容器。
拉取成功后停止 API writer，再启动新 API；迁移完成后才启动 worker/HTTP。
API 部署期间 restart=no；通过容器、8084 和原 URL 的 API 健康检查后才启用 unless-stopped。
应用日志和业务流程/PDF/实际收件仍需按变更风险验收，健康接口不等于完整业务验证。

代码变更含迁移时，仍需人工审阅 Up/Down；危险变更先确认可用备份。
本次保留业务数据，自动部署不会执行 DROP DATABASE、自动备份或自动还原。

## 失败时

不要盲目重跑。/opt/project1-auto/needs-attention 留下目标版本；
迁移开始后失败时脚本尝试停止新 API，保留数据与现场，不自动回滚旧镜像。
管理员先查看容器、日志和实际数据库结构，制定修复方案后再恢复发布。

release.env 记录最后成功的两个镜像 digest，previous-release.env 记录前次成功版本。
这只是版本记录，不是数据库备份，也不代表可在数据库变更后直接回滚。

## Ubuntu 旧文件清理计划：新部署成功后再执行

先列出实际文件、容器、引用及挂载，再按确认的完整路径清理。当前仓库没有执行任何服务器删除。

候选旧部署目录：
- /home/shunbin/project1-ubuntu-practice
- /home/shunbin/project1-ubuntu-production
- /home/shunbin/project1-ubuntu-production-retry
- /home/shunbin/project1-docker-cd-tools-20261020
- /opt/project1-docker、/etc/project1-docker、/var/lib/project1-docker
- 旧原生应用 /opt/project1/api、/var/www/project1（先检查是否含用户文件/仍被引用）

旧权限入口 project1-deploy、project1-migration-prepare、project1-migration-status、
project1-docker-update 及对应 root 工具，确认不再使用后移除。
不要在新部署前清空旧环境文件或删除仍运行容器所依赖的目录。
先移除旧授权，再按清单移除工具；不要使用整个 /usr/local/libexec 的通配删除。

必须保留：SQL Server 服务、Project1Db、业务表和数据、现有迁移历史。
/var/opt/mssql 不是旧部署目录，不能整目录删除。数据库备份/恢复副本暂时保留，
是否单独清理另行确认。不要运行 docker system prune --volumes 等全局清理命令。
新目录 /opt/project1-auto 和主机 Nginx 继续使用，不在清理清单中。

参考：[GitHub 发布镜像](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images)、
[GHCR](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)、
[Compose pull](https://docs.docker.com/reference/cli/docker/compose/pull/)。
