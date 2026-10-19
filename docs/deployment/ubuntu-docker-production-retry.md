# 正式切换回退后的修复重试

本页仅适用于本次 `prod-20261019t120746z-fa24fb6d` 因新备份 `660` 权限停止、
且工具已经成功回退到 `rolled-back` 的情况。旧 API 应为 active/enabled，原网站正常。
助手只在电脑本地修复和测试；下列 Ubuntu 变更由用户逐步执行，不能整页一次运行。

后续现场记录：修正版已重新准备，新备份／恢复和异机副本通过。activate 首次验证失败，
但随后容器、原网址、数据库保护检查和浏览器登录／旧数据读取通过；尚未记录完成。
当前转到 [受控复核指南](ubuntu-docker-activation-recheck.md)，不要再运行本页重试步骤。

2026-10-19 修正版发布记录（已在电脑生成，尚未上传／安装到 Ubuntu）：

- 标签：`prod-20261019t131132z-d37cfd3d`
- 镜像包 SHA256：`ee8260bcb563efbfc09e2ee4ff1bafd1867fbbf53ec4318f5eb1103f0fd05cfd`
- 新工具 SHA256：`09e2a057a08c2b10b65d77ea3aaa45c6ecdc97d8a0162b58fdd952ea8bf058df`
- 本地保护测试 50 项通过；加上原练习测试共 83 项通过。4 项 opt-in 隔离 SQL
  测试另行运行通过，默认套件中的跳过项不冒充已运行。
- 本次未更改业务代码、Compose 或数据库迁移；新包除工具及发布标签外的公开配置哈希、
  前端入口和迁移列表已与旧包核对一致。真实 Ubuntu 上的修复重试尚未执行。

## 修复内容与边界

- SQL Server 可能生成 `660` 的备份，原脚本在收紧权限前执行了严格检查。
- 现在仅对本次新建的备份，通过目录／文件描述符检查归属、普通文件类型、单硬链接、
  精确文件名和权限白名单，再用 `fchmod` 收紧为 `600`，核对 inode 并执行原严格检查。
  不放宽 `regular`，不跟随符号链接，不修改旧备份或整个 SQL 目录的权限。
- `prepare-retry` 只接受 `rolled-back` 和不同的已校验发布标签；要求旧网站、旧文件、
  Nginx 快照、已有 SQL 账号／schema／邮件队列均通过，正式容器不在运行。
- 仅允许修复工具，不允许改变 Compose、Nginx、公开配置模板、迁移列表或前端入口。
- 旧 root-only 状态／配置保存至 `/var/lib/project1-docker/history/`，旧发布包和备份保留。
  私密配置只更新 `PROD_TAG`；不生成密码、不重建账号、不重设 JWT、不执行账号 DDL。
- 准备阶段不中断旧服务，不启动应用、不迁移、不发送邮件。启动应用后原有初始化／后台
  邮件工作仍可能写入原库；这与首次切换相同，不是只读部署。
- 任何中断会保留现场；`retry-preparing` 不是可以继续 freeze 的状态，必须人工审阅。

## 1. 电脑本地验证和新发布包

```powershell
python -m unittest discover -s scripts/docker/tests -p 'test_ubuntu*.py' -v
& .\scripts\docker\Export-UbuntuProduction.ps1 -Apply
```

导出工具只在电脑构建新标签的两个镜像，封装修正版工具和公开文件。不要覆盖旧包。
默认测试跳过 opt-in SQL 测试；隔离 SQL 测试另行显式执行，不连接服务器或读取真实 env。

修正版本地验收须包括：原始 `660` 被严格通用检查拒绝、专用处理收紧后实际备份／恢复／
CHECKDB 通过；错误归属、符号／硬链接、FIFO、全员可写等拒绝；回退后准备的 Linux
文件权限、状态历史和原有 SQL 凭据保留通过。systemd／现场 Nginx 仍须服务器验证。

## 2. 新上传目录【Ubuntu SSH，不停机】

`Tag` 必须替换为电脑刚导出的实际新标签，之后各步相同；不要继续使用旧标签。

```bash
(
set -eu
Tag=REPLACE_NEW_PRODUCTION_TAG
printf '%s\n' "$Tag" | grep -Eq '^prod-[0-9]{8}t[0-9]{6}z-[0-9a-f]{8}$'
Root=/home/shunbin/project1-ubuntu-production-retry
if [ ! -e "$Root" ] && [ ! -L "$Root" ]; then
    mkdir -m 0700 -- "$Root"
fi
[ "$(readlink -f "$Root")" = "$Root" ]
[ "$(stat -c '%U:%G:%a' "$Root")" = shunbin:shunbin:700 ]
[ ! -e "$Root/$Tag" ] && [ ! -L "$Root/$Tag" ]
mkdir -m 0700 -- "$Root/$Tag"
printf '重试上传目录：%s/%s\n' "$Root" "$Tag"
)
```

在电脑 PowerShell 使用原指南的八文件 scp 命令，将 `$Release` 换成新发布目录，
远端目录换为 `/home/shunbin/project1-ubuntu-production-retry/实际新标签/`。
不修改旧 `/home/shunbin/project1-ubuntu-production`；不上传任何私密 env。

## 3. 校验并加载【Ubuntu SSH，不停机】

```bash
(
set -eu
Tag=REPLACE_NEW_PRODUCTION_TAG
cd "/home/shunbin/project1-ubuntu-production-retry/$Tag"
chmod 600 -- images.tar images.tar.sha256 release.json compose.ubuntu.yml \
    .env.docker.ubuntu.example ubuntu_production.py project1-docker.conf project1-legacy.conf
python3 ubuntu_production.py archive --retry-tag "$Tag"
sha256sum -c images.tar.sha256
)
```

通过后，单独执行 `sudo docker load --input images.tar`。只导入镜像，不启动应用。

## 4. 受控更新 root 工具【Ubuntu SSH，不停机】

先核对新工具源码及电脑生成的新 manifest 中 `files_sha256.ubuntu_production.py`。
下面脚本只允许替换本次已知的旧工具；先保留旧工具的 root-only 副本，再原子替换。
必须将 `Tag` 和 `NewHelperSha` 替换为电脑核对的实际值，不能从未经核对的远端文件
直接取值当作信任来源。SHA256 只校验本地可信发布的完整性，不是独立签名。

```bash
Tag=REPLACE_NEW_PRODUCTION_TAG
NewHelperSha=REPLACE_PC_VERIFIED_HELPER_SHA256
sudo -v
sudo /bin/bash -s -- "$Tag" "$NewHelperSha" <<'ROOT'
set -euo pipefail
export LC_ALL=C
Tag=$1
Expected=$2
[[ "$Tag" =~ ^prod-[0-9]{8}t[0-9]{6}z-[0-9a-f]{8}$ ]]
[[ "$Expected" =~ ^[0-9a-f]{64}$ ]]
Tool=/usr/local/libexec/project1-docker.py
Source=/home/shunbin/project1-ubuntu-production-retry/$Tag/ubuntu_production.py
Saved=/usr/local/libexec/project1-docker-before-$Tag.py
[ "$(readlink -f /usr/local/libexec)" = /usr/local/libexec ]
[ "$(stat -c '%U:%G:%a' /usr/local/libexec)" = root:root:755 ]
[ -f "$Tool" ] && [ ! -L "$Tool" ]
[ "$(stat -c '%u:%a:%h' "$Tool")" = 0:644:1 ]
[ "$(sha256sum "$Tool" | cut -d ' ' -f 1)" = 17fe8c4999fdccc456cded25bd9191c8d47c651b03516a314fbcf3a8d64cd4da ]
Phase=$(/usr/bin/python3 -I "$Tool" status | /usr/bin/python3 -I -c 'import json,sys; print(json.load(sys.stdin)["phase"])')
[ "$Phase" = rolled-back ]
[ "$(readlink -f "$Source")" = "$Source" ]
[ -f "$Source" ] && [ ! -L "$Source" ]
[ "$(stat -c '%U:%a:%h' "$Source")" = shunbin:600:1 ]
[ ! -e "$Saved" ] && [ ! -L "$Saved" ]
install -m 0600 -o root -g root "$Tool" "$Saved"
Temporary=$(mktemp /usr/local/libexec/.project1-docker-retry.XXXXXXXX)
trap 'rm -f -- "$Temporary"' EXIT
install -m 0644 -o root -g root "$Source" "$Temporary"
[ "$(sha256sum "$Temporary" | cut -d ' ' -f 1)" = "$Expected" ]
mv -T -- "$Temporary" "$Tool"
printf '修正版工具已安装；旧网站未停止，尚未重新准备或切换。\n'
ROOT
```

任何失败就停止。旧工具副本或已安装新工具不自动覆盖／删除；不要跳过检查再运行。
安装工具本身不改变私密配置、数据库、Nginx 或服务状态。

## 5. 显式重新准备【Ubuntu SSH，不停机】

```bash
(
set -eu
Tag=REPLACE_NEW_PRODUCTION_TAG
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py prepare-retry --retry-tag "$Tag"
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py prepare-retry --apply --retry-tag "$Tag"
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py status
)
```

成功为新标签的 `configured`，旧网站仍运行。不需要再次输入 sa/Gmail 密码，不重新
执行 stage/configure。仅由服务器 root 读取自己的私密文件，不向聊天或电脑传出密码。
之前的 `backup`／`frozen_counts` 不再作为当前尝试的授权，原始数据保留在历史快照。

若为 `retry-preparing`，旧网站仍未被该命令停止，但配置标签／封存文件可能部分更新。
保留现场和所有快照，人工核对后决定处理，不手改状态或删除目录重试。

## 6. 另行确认维护窗口后，重新执行完整切换

回到原指南第 6 步：停止其他业务写入，再执行新的 freeze、生成新文件、恢复演练和
下载验证新异机备份，然后才能 activate。**不要直接运行旧 activate 或复用旧哈希。**
成功后仍须原账号登录、业务数据、PDF、实际 Gmail 收件验证，再 finalize。
旧版本、旧配置、旧备份都保留；新一次失败仍走明确回退，不恢复原数据库。
