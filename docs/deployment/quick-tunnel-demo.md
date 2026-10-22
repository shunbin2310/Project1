# Ubuntu：Quick Tunnel 开机启动与 GitHub Demo 链接更新

适用于已经能通过 `http://127.0.0.1` 打开 Project1 的 Ubuntu。
每天手动开机后，网站服务恢复运行；本服务等待网站就绪，再启动 Quick Tunnel，
确认公网入口和 API 正常后，更新 `shunbin2310/Project1` 的根目录 `Readme.md`。

README 只保留项目名称、技术栈、Live Demo、开放时间。
简历放固定链接 `https://github.com/shunbin2310/Project1` 或个人主页链接；
服务器上的脚本负责更新会变化的 Demo 链接。

## 1. 先把 README 发布到 GitHub

本次修改的根目录 `Readme.md` 必须先通过你平时的 Git 提交／PR 流程合并到 `main`。
安装文件同样可以一起提交。第一次提交正常运行 CI，不要手动加 `[skip ci]`。
打开 GitHub 的 Project1，确认根目录 README 已经显示 Live Demo 和 Availability。
实际文件名是 `Readme.md`，GitHub API 区分大小写；不要改成另一个大小写不同的文件。
原有开发和运行说明保留在 `docs/PROJECT_NOTES.md`，其中的命令仍从仓库根目录运行。

README 中这两个隐藏标记供脚本定位，请保留，各出现一次：

```markdown
<!-- demo-link:start -->
The demo link will appear here after the server starts and the website is reachable.
<!-- demo-link:end -->
```

以后只替换标记之间的内容，不覆盖其他作者对 README 的修改。
如果仓库是 Private，面试官需要仓库权限才能看到 README。

## 2. 把安装文件复制到 Ubuntu

在 **Windows PowerShell，Project1 仓库目录**执行。将 `YOUR_UBUNTU_IP` 替换为
你 SSH 连接 Ubuntu 使用的 IP 或主机名；`shunbin` 是示例中的 Ubuntu 用户名。

```powershell
scp -r .\scripts\homelab shunbin@YOUR_UBUNTU_IP:~/project1-homelab
```

这是首次复制到 `~/project1-homelab` 的命令。以后升级时，可明确复制目录内的文件，
避免已有目录中多出一层 `homelab`：

```powershell
scp .\scripts\homelab\quick_tunnel.py .\scripts\homelab\project1-demo.service .\scripts\homelab\demo.env.example .\scripts\homelab\install.sh shunbin@YOUR_UBUNTU_IP:~/project1-homelab/
```

然后在 **Ubuntu Terminal / SSH** 执行：

```bash
cloudflared --version
python3 --version
cd ~/project1-homelab
sudo bash install.sh
```

安装器写入：

| 路径 | 用途 |
| --- | --- |
| `/opt/project1-demo/quick_tunnel.py` | 自动化脚本 |
| `/etc/systemd/system/project1-demo.service` | 开机服务 |
| `/etc/project1-demo/config` | 仓库名、分支名，不包含 Token |
| `/etc/project1-demo/github-token` | 下一步手动创建的私密 Token 文件 |

安装器不会启动服务，也不会修改现有网站、Compose、数据库、部署脚本。
再次安装会更新脚本和 unit，但保留已有 config 和 Token；升级后需手动重启服务。

## 3. 创建 GitHub Fine-grained Token

GitHub 头像 → **Settings → Developer settings → Personal access tokens →
Fine-grained tokens → Generate new token**。

| 设置 | 值 |
| --- | --- |
| Token name | `project1-demo-link` |
| Expiration | 例如 90 days；到期前更换 |
| Resource owner | `shunbin2310` |
| Repository access | Only select repositories → `Project1` |
| Repository permissions | Contents → Read and write |

不需要 Actions、Workflows、Administration 的写权限。
Token 无法绕过分支保护：目标 `main` 必须允许该账号通过 Contents API 更新 README。
若分支规则要求所有更改必须走 PR，直接更新会失败；先反馈规则，不要为了本服务
关闭现有保护或开启强制推送。

在 **Ubuntu** 执行下面这一整段。Token 输入时不会显示，也不会写入 shell history：

```bash
sudo python3 - <<'PY'
from getpass import getpass
import os
from pathlib import Path

token = getpass('Paste the GitHub token (hidden): ').strip()
if not token or any(c.isspace() for c in token):
    raise SystemExit('Expected one non-empty token; nothing was written.')
path = Path('/etc/project1-demo/github-token')
fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
with os.fdopen(fd, 'w', encoding='utf-8') as output:
    os.fchmod(output.fileno(), 0o600)
    output.write(token + '\n')
print('Token saved. Its value was not printed.')
PY
```

Token 保留在 Ubuntu 的 `root:root 0600` 文件，父目录为 `0700`。
systemd 使用 `LoadCredential` 让动态服务用户读取副本。
不要将 Token 发到聊天、放到 README 或提交到 Git。

## 4. 确认网站自身会开机启动

本服务只负责 Tunnel 和链接，不执行部署、拉取镜像或重跑迁移。
网站仍由现有 Docker、Nginx 和主机 SQL Server 管理。

```bash
systemctl is-active docker nginx mssql-server
systemctl is-enabled docker nginx mssql-server
```

三项应为 active，并且 enabled。若这些现有服务 active 但未 enabled，可以启用开机启动：

```bash
sudo systemctl enable docker nginx mssql-server
```

检查当前部署容器的 restart policy：

```bash
sudo docker ps -a --filter 'label=com.docker.compose.project=project1-auto'
sudo docker ps -aq --filter 'label=com.docker.compose.project=project1-auto' \
  | xargs -r sudo docker inspect --format '{{.Name}} restart={{.HostConfig.RestartPolicy.Name}}'
```

正常成功部署后的 API 和 frontend 应为 `unless-stopped`，且处于运行状态。
仓库里的部署脚本在成功健康检查后才启用 API 的 restart policy。
如果不是这个项目名、没有容器、API 是 `no` 或容器已停止，先检查现有部署，
不要盲目执行 Compose 重建或为失败的迁移开启重试。

确认本机访问仍正常：

```bash
curl -I http://127.0.0.1
curl -fsS http://127.0.0.1/api/health
```

## 5. 停止手动 Tunnel，启动后台服务

在原来运行 `cloudflared tunnel --url ...` 的终端按 **Ctrl+C**，
只停止这次手动测试的 Tunnel。原来的测试链接将失效。

然后在 **Ubuntu** 执行：

```bash
sudo systemctl enable --now project1-demo.service
sudo systemctl status project1-demo.service --no-pager
sudo journalctl -u project1-demo.service -n 80 --no-pager
```

日志应依次出现：

```text
Waiting for the local website and Project1 API
Detected demo URL: https://....trycloudflare.com
README updated: https://....trycloudflare.com
```

第一次新地址可能要等待 DNS／Tunnel 生效；未通过公网检查前不会更新 README。
再打开 GitHub Project1 的 README，用手机移动数据点击 **Open Live Demo**，
验证登录和业务列表。此后可以关闭 SSH 窗口，服务继续运行。

## 6. 验证重启后的行为

先测试只重启 Tunnel 服务：

```bash
sudo systemctl restart project1-demo.service
sudo journalctl -u project1-demo.service -n 80 --no-pager
```

会创建新的 Quick Tunnel 地址，公网检查通过后 README 自动更新。
原地址相同的短暂断线重连，不会反复创建提交。

最后在你方便中断访问时，重启笔电验证完整开机流程：

```bash
sudo reboot
```

重新连接 SSH 后查看服务日志及 GitHub README，再用手机测试。
如果只有服务启动但网站没有恢复，日志会一直等待本机网站；先排查 Docker／SQL／Nginx。

## 日常行为与排查

- 每天手动开机即可。此方案不设置午夜自动关机；你关机后 Live Demo 离线。
- README 的链接不会在关机时自动删除，也不显示实时在线状态；Availability 解释开放限制。
- cloudflared 自己处理短暂断线。进程退出由 systemd 重启，取得新地址后再更新 README。
- GitHub 更新失败不停止正在运行的 Tunnel；每 30 秒重试，且重新读取远程 README 和 SHA。
- 自动链接提交包含 `[skip ci]`，避免触发现有 push CI/CD；普通代码提交正常运行 CI。
- 本脚本使用标准 Python 库，不需要 pip install，也不依赖本地 Git checkout。

常用命令：

```bash
sudo systemctl status project1-demo.service --no-pager
sudo journalctl -u project1-demo.service -n 100 --no-pager
sudo systemctl restart project1-demo.service
```

| 日志 / 现象 | 检查 |
| --- | --- |
| Waiting for the local website | 本机首页和 `/api/health`，Docker／SQL／Nginx |
| Waiting for the local and public website/API | 新地址的 DNS／Tunnel 是否生效，网络是否恢复 |
| GitHub HTTP 401 | Token 是否无效或过期 |
| GitHub HTTP 403 / 429 | Contents 写权限、分支规则或 API 限流 |
| GitHub HTTP 404 | 仓库名、分支、Token 授权和根目录 README 是否已发布 |
| GitHub HTTP 409 / 422 | 并发提交或分支规则；脚本下次重新获取 README，再检查 GitHub 规则 |
| README update failed (ValueError) | README 是否各保留一个 start/end 标记，顺序是否正确 |
| No Quick Tunnel URL appeared | cloudflared 日志、网络，以及已有 `/etc/cloudflared` 配置是否影响 Quick Tunnel |

更换 Token：重复第 3 步的隐藏输入命令，再执行 `sudo systemctl restart project1-demo.service`。

停用服务及开机启动：

```bash
sudo systemctl disable --now project1-demo.service
```

此命令停止 Demo Tunnel，不停止现有网站容器。

## 本地验证

在仓库目录执行（不访问真实 Cloudflare 或 GitHub）：

```powershell
python -m unittest discover -s scripts/homelab/tests -v
```

## 官方参考

- [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/)
- [GitHub 更新文件 API](https://docs.github.com/en/rest/repos/contents#create-or-update-file-contents)
- [GitHub Fine-grained Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)
- [GitHub 跳过指定提交的 CI](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/skip-workflow-runs)
- [systemd LoadCredential](https://www.freedesktop.org/software/systemd/man/latest/systemd.exec.html#LoadCredential=ID[:PATH])
