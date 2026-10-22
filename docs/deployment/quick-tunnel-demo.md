# Ubuntu：Quick Tunnel 开机启动与固定 Demo 入口

面试官的访问路径：

```text
简历 → GitHub Project1 → Open Live Demo
     → https://shunbin2310.github.io/Project1/
     → 当天最新的 Quick Tunnel → Project1
```

根目录 `Readme.md` 使用固定 Pages 链接，随本次 PR 修改一次。
Ubuntu 等待本机网站就绪，启动 Tunnel，确认公网首页和 API 正常后，
将最新地址写到 `demo-links` 分支的 `index.html`，由 GitHub Pages 发布跳转页。
`main` 继续保留必须通过 PR 和两项 CI 检查的规则。

## A. 已安装旧版本：按此升级

如果已经安装服务和 Token，并遇到 `HTTP 409: Changes must be made through a pull request`，
从本节开始。已有 Token 和网站配置可以继续使用。

### A1. 合并修正

将本次修改按正常 PR 流程合并到 `main`，完成现有 CI。
确认 GitHub 根目录 README 的 **Open Live Demo** 已指向固定 Pages 地址。

### A2. 上传并重启新版服务

在 **Windows PowerShell，Project1 仓库目录**执行。
`192.168.1.125` 是此次安装使用的 Ubuntu 地址；若已变化，请替换。

```powershell
scp .\scripts\homelab\quick_tunnel.py .\scripts\homelab\project1-demo.service .\scripts\homelab\demo.env.example .\scripts\homelab\install.sh shunbin@192.168.1.125:~/project1-homelab/
```

在 **Ubuntu Terminal / SSH** 执行：

```bash
cd ~/project1-homelab
sudo bash install.sh
sudo systemctl restart project1-demo.service
sudo journalctl -u project1-demo.service --since '2 minutes ago' --no-pager
```

安装器保留已有 config 和 Token。新版固定使用 `demo-links`，
旧 config 中的 `GITHUB_BRANCH=main` 会被忽略，无需手动修改。
重启服务会结束旧 Tunnel，生成新地址。

预期日志：

```text
Detected demo URL: https://....trycloudflare.com
Redirect updated on demo-links: https://....trycloudflare.com
```

这表示分支已经更新；还需下一节的一次性 Pages 设置。

### A3. 开启 GitHub Pages（只做一次）

脚本第一次成功发布时自动创建 `demo-links`。
不要提前从 `main` 手动创建该分支，也不要将它合并到 `main`。

打开 **GitHub Project1 → Settings → Pages**，在 **Build and deployment** 设置：

| 设置 | 值 |
| --- | --- |
| Source | Deploy from a branch |
| Branch | demo-links |
| Folder | / (root) |

点击 **Save**。若看不到 `demo-links`，先确认 A2 的成功日志，再刷新设置页面。
如果仓库已有 Pages 网站，先确认它的用途；更换来源会替换该仓库现有 Pages 内容。

GitHub 官方说明发布可能需要最多约 10 分钟。
在 **Actions** 查看 Pages 的构建／发布任务；它与 Project1 的应用 CI/CD 分开。
现有应用工作流只监听 `main`，不会因 `demo-links` 提交而构建或部署应用镜像。

Pages 生效后，**Settings → Pages → Visit site** 应打开固定入口：

```text
https://shunbin2310.github.io/Project1/
```

浏览器自动跳转到最新 Tunnel；页面还提供手动链接。
JavaScript 不可用时仍可通过 HTML 自动跳转或点击链接访问。

### A4. 验证

1. 手机关闭 Wi-Fi，用移动数据打开 GitHub Project1。
2. 点击 README 的 **Open Live Demo**，确认自动进入网站。
3. 测试登录和业务列表。
4. 关闭 SSH 窗口，确认仍能访问。

然后在方便中断访问时重启笔电：

```bash
sudo reboot
```

重新连接 SSH 后检查：

```bash
sudo systemctl status project1-demo.service --no-pager
sudo journalctl -u project1-demo.service -b -n 100 --no-pager
```

确认 `Redirect updated on demo-links`，等待 Pages 发布完成后再用手机点击相同入口。
发布期间旧 Tunnel 可能已经失效，Pages 还没发布新内容；稍等后刷新或用无痕窗口重试。

## B. 新机器：首次安装

已经完成 A 节的机器无需重复安装。

### B1. 网站和安装文件

网站、Docker、Nginx、主机 SQL Server 应已正常部署。
在 **Ubuntu** 确认：

```bash
curl -I http://127.0.0.1
curl -fsS http://127.0.0.1/api/health
cloudflared --version
python3 --version
```

先将本次根目录 `Readme.md` 和脚本按正常 PR 流程合并到 `main`。
在 **Windows PowerShell，仓库目录**执行，将 `YOUR_UBUNTU_IP` 换成实际 SSH 地址：

```powershell
scp -r .\scripts\homelab shunbin@YOUR_UBUNTU_IP:~/project1-homelab
```

这是首次复制到该目录的命令，重复上传请使用 A2 的明确文件列表。
然后在 **Ubuntu** 执行：

```bash
cd ~/project1-homelab
sudo bash install.sh
```

安装器只安装文件，不启动服务：

| 路径 | 用途 |
| --- | --- |
| `/opt/project1-demo/quick_tunnel.py` | 自动化脚本 |
| `/etc/systemd/system/project1-demo.service` | 开机服务 |
| `/etc/project1-demo/config` | 仓库名，不包含 Token |
| `/etc/project1-demo/github-token` | 下一步手动创建的私密 Token 文件 |

### B2. GitHub Token

现有 Project1 Fine-grained Token 有 **Contents: Read and write** 时可以复用。
首次创建：GitHub 头像 → **Settings → Developer settings → Personal access tokens →
Fine-grained tokens → Generate new token**。

| 设置 | 值 |
| --- | --- |
| Token name | project1-demo-link |
| Expiration | 例如 90 days，到期前更换 |
| Resource owner | shunbin2310 |
| Repository access | Only select repositories → Project1 |
| Repository permissions | Contents → Read and write |

不需要 Actions、Workflows、Administration 或 Pages 的写权限。
Pages 发布来源通过 GitHub 设置页面配置。
Token 需要能创建并更新 `demo-links`；若该分支也被通用规则要求 PR／CI，先反馈具体规则。

在 **Ubuntu** 执行下面整段代码。提示出现后才粘贴 Token，输入不会显示：

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

Token 文件为 `root:root 0600`，父目录为 `0700`。
systemd 使用 `LoadCredential` 让动态服务用户读取副本。
不要将 Token 发到聊天或提交到 Git。

### B3. 确认网站会开机恢复

```bash
systemctl is-active docker nginx mssql-server
systemctl is-enabled docker nginx mssql-server
```

三项应为 active 和 enabled。若现有服务 active 但未 enabled，可执行：

```bash
sudo systemctl enable docker nginx mssql-server
```

检查当前容器和 restart policy：

```bash
sudo docker ps -a --filter 'label=com.docker.compose.project=project1-auto'
sudo docker ps -aq --filter 'label=com.docker.compose.project=project1-auto' \
  | xargs -r sudo docker inspect --format '{{.Name}} restart={{.HostConfig.RestartPolicy.Name}}'
```

成功部署后的 API 和 frontend 应运行健康，restart policy 为 `unless-stopped`。
如果没有容器、项目名不同或 API 是 `no`，先检查部署。
本服务不负责重新部署、拉取镜像或为失败的迁移开启重试。

### B4. 启动

若手动测试的 cloudflared 仍运行，在那个终端按 Ctrl+C 停止手动 Tunnel。

```bash
sudo systemctl enable --now project1-demo.service
sudo journalctl -u project1-demo.service -n 80 -f
```

看到 `Redirect updated on demo-links` 后，继续 **A3 开启 Pages → A4 验证**。
Ctrl+C 只退出日志查看，不停止后台服务。

## 日常行为和限制

- 每天手动开机即可；此方案不设置午夜自动关机。
- 网站关机后 Live Demo 离线；Pages 入口仍指向上次的 Tunnel。README 的 Availability 解释开放限制。
- Pages 只托管跳转页面；Vue、API 和数据库仍在 Ubuntu 上运行。
- 发布前检查本机和公网首页，以及 Project1 `/api/health` 的 JSON 内容。
- GitHub 失败每 30 秒重试，每次重新取得文件 SHA；冲突不会强制覆盖分支历史。
- cloudflared 处理短暂断线，进程退出由 systemd 重启并创建新 Tunnel。
- 原地址相同的重连不反复提交，地址变化才更新跳转页。
- 首次创建 `demo-links` 使用独立根提交，仅含 `index.html` 和 `.nojekyll`，没有应用代码或 CI workflow。
- 同名分支没有本脚本页面标记时，脚本拒绝覆盖；不要将 `demo-links` 合并到 `main`。
- `index.html` 由脚本生成，请不要手动编辑。额外文件不会被更新操作修改。
- 发布提交不带 `[skip ci]`，让 Pages 正常发布；应用 CI/CD 只监听 `main`。
- 分支更新成功不代表 Pages 已立即生效，还需要 Pages 发布和缓存传播时间。
- Python 只使用标准库，不需要 pip install 或服务器 Git checkout。

## 排查与维护

```bash
sudo systemctl status project1-demo.service --no-pager
sudo journalctl -u project1-demo.service -n 100 --no-pager
sudo systemctl restart project1-demo.service
```

| 日志 / 现象 | 检查 |
| --- | --- |
| Waiting for the local website | 本机首页、`/api/health`，Docker／SQL／Nginx |
| Waiting for the local and public website/API | 新地址的 DNS、Tunnel 和网络 |
| GitHub HTTP 401 | Token 是否失效或过期 |
| GitHub HTTP 403 / 429 | Contents 写权限或 API 限流，结合 GitHub message 判断 |
| GitHub HTTP 404 | 仓库名和 Token 的仓库授权；分支首次不存在时会自动初始化 |
| GitHub HTTP 409 / 422 | 查看 message，检查 demo-links 的 PR／CI 限制或并发创建／更新 |
| Redirect update failed (ValueError) | 同名分支是否缺少受管理的 index.html，或页面是否缺少唯一标记 |
| Redirect updated，但入口 404 | Pages 是否选 demo-links / (root)，Pages 部署是否完成 |
| 固定入口跳到旧网址 | 等待发布和缓存刷新，查看 Actions，用无痕窗口测试 |
| No Quick Tunnel URL appeared | cloudflared 日志、网络、已有 `/etc/cloudflared` 配置 |

HTTP 错误只显示经过长度限制和 Token 隐去处理的 GitHub `message`，
不打印 Token、Authorization header 或完整响应体。
更换 Token 后重启服务。

停用 Tunnel 和开机启动（保留网站容器）：

```bash
sudo systemctl disable --now project1-demo.service
```

## 本地验证

不访问真实 Cloudflare 或 GitHub：

```powershell
python -m unittest discover -s scripts/homelab/tests -v
```

## 官方参考

- [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/)
- [GitHub Pages 发布来源](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site)
- [GitHub Pages 创建与发布时间](https://docs.github.com/en/pages/getting-started-with-github-pages/creating-a-github-pages-site)
- [GitHub 更新文件 API](https://docs.github.com/en/rest/repos/contents#create-or-update-file-contents)
- [GitHub Git trees](https://docs.github.com/en/rest/git/trees#create-a-tree)
- [GitHub Git commits](https://docs.github.com/en/rest/git/commits#create-a-commit)
- [GitHub Git refs](https://docs.github.com/en/rest/git/refs#create-a-reference)
- [GitHub Fine-grained Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)
- [systemd LoadCredential](https://www.freedesktop.org/software/systemd/man/latest/systemd.exec.html#LoadCredential=ID[:PATH])
