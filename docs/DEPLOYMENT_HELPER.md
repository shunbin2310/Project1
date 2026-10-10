# Project1 受限部署工具与手动 CD

2026-10-08：用户已在 Ubuntu 验证原有 58 项沙盒测试全部通过，完成 root 管理的工具安装、
完整 sudoers 校验及 `project1_deploy` 免密 `--help` 测试。授权仅限固定工具，没有通用 sudo 权限。
**尚未执行真实部署。** 这是当日用户提供输出所确认的状态，不代表未来服务器状态自动保持不变。

仓库新增 `.github/workflows/deploy-manual.yml`，默认只校验产物，必须先经 PR 合并到 `main` 再使用。
现有 `ci.yml` 和 `tailscale-check.yml` 不变；不会因为 push 或 PR 自动部署。

## 文件

- `scripts/deploy/project1_deploy.py`：仅使用 Python 标准库的固定路径部署工具。
- `scripts/deploy/project1-deploy`：未来 root 安装的入口，使用 `/usr/bin/python3 -I` 启动固定脚本。
- `scripts/deploy/project1-deploy.sudoers`：待审核模板，只授权这个入口；不是 `NOPASSWD: ALL`。
- `scripts/deploy/tests/test_project1_deploy.py`：临时目录、假服务控制和假 HTTP 测试，不连接真实应用。
- `scripts/deploy/verify_ci_artifacts.py`：绑定指定 CI run 和完整 SHA，下载、校验两个 ZIP 及布局；不连接服务器。
- `scripts/deploy/deploy_verified.py`：仅在明确确认的 main 手动工作流中，用固定 SSH 用户/主机和严格主机密钥校验上传、调用工具。
- `scripts/deploy/tests/test_manual_cd.py`：模拟 GitHub API、ZIP 和 SSH 的离线测试，不使用真实凭据。

`-I` 隔离模式忽略 Python 环境变量，并排除当前目录/脚本目录等不可信导入来源。
入口、Python 文件及其父目录必须都由 root 管理，部署用户不能修改它们。
参见 [Python 隔离模式](https://docs.python.org/3/using/cmdline.html#cmdoption-I) 和
[sudoers 文档](https://manpages.ubuntu.com/manpages/noble/man5/sudoers.5.html)。

## 固定契约

固定安装位置（已经由用户手动安装，未来更新也需要管理员单独审核）：

| 用途 | 固定路径 / 权限 |
| --- | --- |
| 入口 | `/usr/local/sbin/project1-deploy`，root:root，0755 |
| 实现 | `/usr/local/libexec/project1-deploy.py`，root:root，0644 |
| 事务状态 | `/var/lib/project1-deploy`，root:root，0700 |
| 上传目录 | `/home/project1_deploy/uploads/<COMMIT_SHA>/`，project1_deploy，0700 |
| 上传文件 | 上述目录的 `api.zip` 和 `frontend.zip`，project1_deploy，0600 |
| API | `/opt/project1/api`，root:project1，目录 0750 / 文件 0640 |
| 前端 | `/var/www/project1`，root:www-data，目录 0750 / 文件 0640 |
| 唯一控制的服务 | `project1-api.service` |

调用契约是 **40 位小写提交 SHA、64 位 API ZIP SHA-256、64 位前端 ZIP SHA-256**。
没有任意路径、任意服务、shell 命令、环境覆盖或 `--force` 参数。
`--help` 只显示帮助，可在普通用户/Windows 上运行。

调用契约（不要手动填占位值运行）：

```text
project1-deploy COMMIT_SHA API_ZIP_SHA256 FRONTEND_ZIP_SHA256
```

ZIP 顶层必须直接包含 publish/dist 内容，不能多包一层 `api/` 或 `dist/`。
API 需要 `Project1.Api.dll`、`Project1.Api.deps.json` 和 `.NET 10` 的
`Project1.Api.runtimeconfig.json`；前端需要 `index.html` 和 `assets/` 下的文件。

## 部署与回滚行为

1. 检查固定路径及全部父目录、当前代码目录的 root 所有权和权限；拒绝符号链接、硬链接、特殊文件和可写代码树。
2. 对 state 目录持有排他锁。若已有 `pending.json`，拒绝新部署，先由管理员处理上次中断。
3. 核对服务的运行用户、组、WorkingDirectory、NoNewPrivileges、FragmentPath、DropInPaths 和 active 状态。
   安装审核还必须确认服务实际以 `project1` 运行 `/usr/bin/dotnet /opt/project1/api/Project1.Api.dll`，
   没有从代码目录执行的特权启动钩子；该工具不会更改服务定义或执行 daemon-reload。
4. 先检查旧 API/前端的健康状态，再以不跟随链接的文件描述符读取上传 ZIP。
   只读取普通、单硬链接、归部署用户所有的文件，复制到 root 私有快照后核对 SHA-256。
5. 拒绝 ZIP 路径越界、重复/大小写冲突、链接、设备/FIFO、权限提升位、加密、非 stored/deflate 压缩和配置密钥文件。
   限制每个 ZIP 256 MiB、每个文件 128 MiB、解包总量每包 512 MiB、每包最多 10,000 个条目。
   各相关文件系统至少要有 2 GiB 可用空间。ZIP 的可执行/所有者权限不会被保留。
6. 两个包都验证、解包并设好权限后，持久化事务记录，才停止 API。
7. 在各自父目录内重命名旧目录为备份，再将候选目录切换为活动目录。API/前端不是一个跨目录原子切换，API 会短暂停机。
8. 启动 API，轮询约 60 秒；HTTP 请求和服务命令也有独立超时。
   检查直接 API `127.0.0.1:5000/api/health`、Nginx 代理 `/api/health`、服务 active 状态，
   并比较 Nginx `/index.html` 的响应哈希与本次前端文件。拒绝 HTTP 重定向，不使用环境中的 HTTP 代理。
9. 成功后记录 `committed`，保留旧版备份和 ZIP 快照；不自动清理旧版本。
10. 启动/健康检查/中途切换失败时，先停止新 API，再恢复旧目录并启动、检查旧版。
    已切换的新代码留在 failed 目录，不悄悄覆盖旧备份。
11. 如果恢复也失败，保留 `pending.json`，记录 `recovery_required`，拒绝重试。
    kill、停电或不可捕获的中断不能保证自动恢复，必须由管理员按事务记录检查实际目录和服务。

每次尝试使用内部随机事务 ID；备份分别位于 `/opt/project1/.api-backup-<ID>` 和
`/var/www/.project1-backup-<ID>`。私有日志在 `/var/lib/project1-deploy/<ID>/transaction.json`，
包含提交、ZIP 哈希、阶段和目录位置，不包含生产环境密钥。
验证失败可能留下私有快照或未激活的候选目录，但不会停止服务或改动活动代码。

## 安全边界与限制

- 工具不读取或修改 `/etc/project1/project1.env`，不读取 SSH 私钥，不修改 Nginx/systemd 配置，不调用数据库或迁移工具。
- 当前代码目录必须只存可替换的发布文件。安装前必须人工检查是否有上传文件、证书、本地配置、Data Protection 密钥等持久数据。
  当前 API 顶层若存在 `appsettings.Production.json`、`.env*`、`uploads`、`data`、`.aspnet`，会直接拒绝部署。
  这不是对所有自定义存储目录的自动发现；不明确时先停止部署设计。
- 健康接口目前只是应用存活检查，不代表数据库、邮件、PDF 或所有业务功能正常；首次部署后仍需业务冒烟测试。
- **回滚仅针对代码，不回滚数据库。** 应用重启会执行既有初始化和后台任务，可能写数据库或处理待发送邮件；
  该工具没有执行迁移，不代表应用运行期间数据库只读。需要新数据库结构的版本必须先单独审核备份和迁移。
- SHA-256 校验保证 ZIP 与输入哈希一致，**不能自行证明包来自可信 CI**。
  新手动工作流核对固定仓库名及 ID、`ci.yml` 的 workflow ID/path、`main` 的 push 事件、完整提交 SHA、
  成功完成的运行、同一 run 的两个产物及 GitHub 返回的 digest。拒绝缺失、重复、过期产物。
  下载后重新核对 run attempt，拒绝校验中重跑的 CI；不使用 PR 产物或模糊的“最新 ZIP”。
- 授权部署本质上允许更新应用代码并以 `project1` 的应用权限运行它，包括访问应用已经拥有的数据库权限。
  受限 sudo 工具不是应用沙箱。需要严格保护部署私钥，并限制哪些可信 main 工作流可使用它。
- 不能只给上传脚本加 sudo 再执行；root 入口/实现必须由管理员安装到固定位置，不能由部署用户升级自身。
- 不自动删除备份、失败目录、上传文件或快照，管理员后续按明确保留策略管理磁盘。
- 遇到 `pending.json` 不要直接删除它并重跑，也不要 `rm -rf` 活动目录。
  先保留记录、核对 API 是否运行、哪个目录是旧版、哪个是候选/失败版，再制定恢复步骤。

## 本地验证

电脑 PowerShell，仓库根目录：

```powershell
python -m unittest discover -s scripts/deploy/tests -v
python scripts/deploy/project1_deploy.py --help
```

Ubuntu 沙盒验证（以普通用户运行，不需要 sudo；新增测试也需要复制对应新脚本及工作流用于静态检查）：

```bash
python3 -m unittest discover -s scripts/deploy/tests -v
python3 scripts/deploy/project1_deploy.py --help
```

便携测试仅在临时目录运行，模拟 root 权限校验、上传快照和服务控制。
Linux 专用测试在用户 home 的临时目录中检查真实 O_NOFOLLOW、FIFO 拒绝、文件权限、flock 和请求超时信号。
Windows 会明确跳过这些 Linux 专用测试；通过便携测试不能代替 Ubuntu 验证或特权安装审核。
任何测试都不会运行真实 systemctl、连接真实健康接口，或访问真实数据库/生产环境密钥。

## 手动 CD 使用顺序

1. 审阅新脚本及工作流，提交这些仓库文件并通过 PR 合并到 `main`。本阶段没有自动提交、push、创建 PR 或部署。
2. 等待这次合并触发的 **Project1 CI / main / push** 完整成功。不要选择 PR CI 或手动触发的 CI。
3. 从该 CI 页面复制 URL 中 `actions/runs/<数字>` 的 **run ID**（不是 `#16` 这种运行序号），以及该运行的完整 40 位小写提交 SHA。
4. Actions → **Project1 manual deployment** → Run workflow → Branch 选择 `main`。
   填入 `ci_run_id`、`commit_sha`，第一次保持 **deploy 不勾选**。
5. 默认模式只有 validate job：运行工具测试、读取 GitHub Actions API、下载两个 ZIP、校验 GitHub SHA-256 digest，
   并在 runner 临时目录检查归档结构、CRC、大小及 .NET 10 配置。**不使用部署 secrets/OIDC、不加入 Tailscale、不 SSH、不写服务器。**
6. 验证成功后先查看结果。准备第一次真实部署前，确认服务器在线、当前业务可接受短暂停机、数据库备份及版本兼容性。
   初次上线不要直接部署需要数据库结构变更的版本。API 启动时仍可能执行既有 seed/邮件后台任务，代码回滚不撤销其数据库写入。
7. 只有你明确决定部署时，重新使用相同 run ID/SHA 并勾选 **deploy**。
   deploy job 在全新 runner 上重新下载校验产物，并要求 run attempt 和两个包哈希与 validate job 完全一致；
   之后通过现有 Tailscale OIDC、OpenSSH 私钥和已固定的主机公钥连接。
   上传到 `/home/project1_deploy/uploads/<SHA>/`，再调用受限工具；不会从上传包执行 root shell 脚本。
8. 部署完成后检查 API/前端健康结果，再人工测试登录、页面与关键业务。健康接口不是完整业务验收。

### 凭据、失败与重试

迁移历史的可选只读预检查见 [MIGRATION_PRECHECK.md](MIGRATION_PRECHECK.md)。默认包验证和应用部署行为保留；
`check_migrations` 与 `deploy` 互斥。查询依赖管理员另行安装的无参数 status 工具，不升级原部署 helper 的权限。

- 使用已有四个 repository secrets：`TS_OAUTH_CLIENT_ID`、`TS_AUDIENCE`、`DEPLOY_SSH_PRIVATE_KEY`、`DEPLOY_SSH_KNOWN_HOSTS`。
  不把值写到工作流，不打印私钥、GitHub bearer token、签名下载 URL 或任意 HTTP/远程错误正文。
- validate job 只有 contents/actions read；deploy job 另有 id-token write。PR/push 不会触发该工作流。
  不添加 `environment:`，避免改变现有 Tailscale main/手动事件 OIDC subject；自动部署需要未来单独修改信任条件并审核。
- Actions API 身份验证请求不跟随重定向。ZIP 使用 GitHub 返回的 HTTPS storage URL 新建无 bearer token 请求，
  仅允许 `.blob.core.windows.net` / `.githubusercontent.com` 后缀，不接受进一步重定向；未来 GitHub 更换存储域名会安全失败，需要审核后更新。
- 每个 ZIP 最多 256 MiB；不存在 GitHub digest、布局不匹配或哈希不符时拒绝部署，而不是放宽校验。
- 同一手动 CD workflow 不并发、不自动取消正在执行的部署；但手动取消、job/SSH 超时、网络断开仍可能让服务器状态不确定。
  这时不要直接重跑，管理员必须先检查服务和 `/var/lib/project1-deploy` 的事务记录。
- 上传目录用普通部署用户创建；同一个 SHA 目录已存在时失败，不覆盖旧上传，也不自动删除上传或备份。
  网络/上传/部署失败后由管理员审核目录和事务再决定后续步骤，不能仅删除 `pending.json` 解除保护。
- 服务器 root 工具不会通过 CI 自我升级，新增这两个 runner 脚本不需要替换已经验证安装的 Python helper。
- 手动 CD 的 validate job 和 PR CI 都运行部署工具的离线 Python 测试；不因此取得生产 SQL 执行权限。
  数据库变更另按[手动迁移检查单](MANUAL_MIGRATIONS.md)处理。

参考：[Python ZIP 安全注意事项](https://docs.python.org/3/library/zipfile.html#decompression-pitfalls)、
[Python 文件描述符接口](https://docs.python.org/3/library/os.html#os.open)、
[GitHub artifact 元数据与下载](https://docs.github.com/en/rest/actions/artifacts)。
