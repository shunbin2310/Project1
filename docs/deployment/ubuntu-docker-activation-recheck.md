# 已运行但激活记录失败：受控复核

本页只适用于 `prod-20261019t131132z-d37cfd3d`，切换工具为 SHA256
`09e2a057a08c2b10b65d77ea3aaa45c6ecdc97d8a0162b58fdd952ea8bf058df`。
用户输出已确认两个容器健康、80/8084 网页版本和 API 检查通过、数据库保护检查通过、
旧 API 停止，浏览器原账号登录和旧数据读取通过。状态仍是 `activation-failed`。
这些结果证明当前可用，**不能还原或证明初次激活失败的具体原因**。

## 范围与保护

独立工具 `scripts/docker/recheck_ubuntu_activation.py` 不修改已发布镜像或原切换工具。
仅由管理员安装到 `/usr/local/libexec/project1-docker-recheck.py`，使用 `python3 -I -B`。
它验证原切换工具的固定哈希后加载其保护函数，共用原有锁，且只接受上述标签和
`activation-failed`。不是跳过检查、手改 JSON 或重新执行 activate。

检查包含：完整封存发布清单、已加载镜像、异机备份确认值与服务器备份、旧文件与
Nginx 快照、旧 API inactive/disabled、Nginx 配置及精确文件哈希、原库受限账号／迁移／
无 Pending 邮件、恰好两个运行服务、实际容器标签／健康／镜像内容，以及两个入口连续
三轮检查。实际镜像比较 runnable layers/config，兼容 config ID 与 manifest index ID
不同的存储方式，不仅比较标签文字。不输出 runtime env、完整 inspect 或私密配置。

`--check` 不改部署状态；`--apply` 重复全部检查，在 root-only history 中保存原失败状态，
再原子记录 `active-pending-verification`。保留原备份和历史，初始失败原因明确记为
`undetermined`。不直接记为 active，不删除失败记录。

**不启动／重建／停止／重启容器，不 reload Nginx，不改 env、用户密码或数据库业务数据，
不执行迁移，不发送邮件。** 仍须单独完成业务、PDF、实际 Gmail 收件验收，再用原工具
finalize。任何失败都停止，不盲目重跑、不回写任意阶段。

本地验证：新增 12 项离线保护测试；默认套件共 95 项通过、4 项 opt-in 跳过。另行显式
运行 4 项隔离 SQL 集成测试通过，覆盖实际 Docker 镜像身份比对、Linux 上只读复核保持
状态不变、保存失败快照后推进 pending、原配置和 SQL 凭据保留。测试中的服务／路由仍为
隔离 fixture，不是现场 Nginx/systemd 的验收；服务器复核由用户执行。

复核文件 SHA256：`224fdf6de6d54e38457891d9725c05c2a17766e59e6b0e8bc9933c986f731100`。

## 1. 上传独立文件【电脑 PowerShell，仓库目录】

```powershell
scp -o StrictHostKeyChecking=yes .\scripts\docker\recheck_ubuntu_activation.py `
    shunbin@192.168.1.158:/home/shunbin/project1-ubuntu-production-retry/prod-20261019t131132z-d37cfd3d/
if ($LASTEXITCODE -ne 0) { throw '传输失败，停止。' }
```

仅增加这个独立文件，不覆盖原发布清单内的任何文件，不上传私密 env 或备份。

## 2. 收紧上传权限、核对、安装一次【Ubuntu SSH，不停机】

先把上传文件权限收紧为 600，再核对电脑提供的 SHA256。目标工具已存在时停止，不覆盖。
临时文件在 root 目录中核对后才改名成为已安装工具，不直接以 root 运行上传目录代码。

```bash
(
set -eu
Source=/home/shunbin/project1-ubuntu-production-retry/prod-20261019t131132z-d37cfd3d/recheck_ubuntu_activation.py
chmod 600 -- "$Source"
sha256sum "$Source"
)
```

哈希匹配上面固定值后，单独执行：

```bash
sudo -v
sudo /bin/bash -s <<'ROOT'
set -euo pipefail
export LC_ALL=C
Expected=224fdf6de6d54e38457891d9725c05c2a17766e59e6b0e8bc9933c986f731100
Source=/home/shunbin/project1-ubuntu-production-retry/prod-20261019t131132z-d37cfd3d/recheck_ubuntu_activation.py
Destination=/usr/local/libexec/project1-docker-recheck.py
[ "$(readlink -f /usr/local/libexec)" = /usr/local/libexec ]
[ "$(stat -c '%U:%G:%a' /usr/local/libexec)" = root:root:755 ]
[ "$(readlink -f "$Source")" = "$Source" ]
[ -f "$Source" ] && [ ! -L "$Source" ]
[ "$(stat -c '%U:%a:%h' "$Source")" = shunbin:600:1 ]
[ ! -e "$Destination" ] && [ ! -L "$Destination" ]
Temporary=$(mktemp /usr/local/libexec/.project1-recheck.XXXXXXXX)
trap 'rm -f -- "$Temporary"' EXIT
install -m 0644 -o root -g root "$Source" "$Temporary"
[ "$(sha256sum "$Temporary" | cut -d ' ' -f 1)" = "$Expected" ]
mv -T -- "$Temporary" "$Destination"
printf '独立复核工具已安装；未改变部署状态或服务。\n'
ROOT
```

## 3. 只检查【Ubuntu SSH】

```bash
sudo /usr/bin/python3 -I -B /usr/local/libexec/project1-docker-recheck.py --check \
    --backup-sha256 874218f7103657394d8714eccc936b8fc640a5a6eb98e0de1ccb9f89b9076aed \
    --backup-bytes 13588992
```

这是已在电脑受限目录验证过的备份确认值，不能随意替换。若当前已不处于上述失败场景，
或有任何 STOP，先检查，不重新激活／重启服务。继续保持维护，暂不新增或重发业务记录。

## 4. 全部通过后，单独确认记录【Ubuntu SSH】

```bash
sudo /usr/bin/python3 -I -B /usr/local/libexec/project1-docker-recheck.py --apply \
    --backup-sha256 874218f7103657394d8714eccc936b8fc640a5a6eb98e0de1ccb9f89b9076aed \
    --backup-bytes 13588992
sudo /usr/bin/python3 -I /usr/local/libexec/project1-docker.py status
```

必须看到 `active-pending-verification` 才开始专门测试记录的业务闭环、PDF 和测试邮件。
原数据库会真实保存这些测试写入；不要修改真实订单或发给真实供应商，先检查所有收件人。
全部验收后再按照原指南执行 finalize；本工具本身不作业务验收结论。
