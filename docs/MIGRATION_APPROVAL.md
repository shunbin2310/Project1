# 迁移批准记录：离线校验

这是迁移 CD 的准备步骤，**不是生产迁移执行器，也不是授权系统**。
`scripts/deploy/migration_approval.py` 只读取指定文件、重新验证已有迁移 ZIP、比较批准记录与历史快照。
没有网络请求、SSH、SQL 执行、上传、服务重启、账号创建或权限修改，也不生成/消费批准记录。
现有工作流及 Ubuntu 上的工具不变；`project1_migrate` 继续只读。

## 校验哪些内容

- 固定仓库 `shunbin2310/Project1`、服务器 `homelab-server` 和数据库 `Project1Db`。
- 正整数 CI run ID / attempt、完整小写提交 SHA、迁移 ZIP SHA-256、SQL SHA-256。
- 已应用历史必须是产物迁移清单的完整前缀；批准的待执行编号必须是全部剩余编号，且不能为空。
- 提供的 status 必须是只读工具的原始 JSON，不能拿 Actions Summary 或自行拼出的报告替代。
  status 的身份/字段/历史会重新检查；历史发生变化时拒绝。
- 审阅 SQL、目标结构和恢复方案三个确认值，以及备份验证确认值，必须是 JSON `true`，不能是字符串或数字。
- JSON 字段必须恰好符合下方格式；重复、缺失、额外字段、错误类型和无效编号会拒绝。
- 文件有大小限制，拒绝空文件、目录、符号链接及多硬链接文件。错误输出不回显输入内容。

时间策略是本阶段的保守默认值，不代表所有数据库的备份都适用同一个时间窗口：

- 所有时间明确使用 UTC（`Z` 或 `+00:00`）。
- 批准有效期最多 1 小时，且当前时间必须处于 `[approved_at, expires_at)`。
- 历史查询距离校验最多 5 分钟，不接受未来时间。
- 备份必须在批准前完成，距离校验最多 24 小时；后续有新写入或新的迁移计划时，仍需重新评估并制作新备份。

## 记录格式

**以下只是不能通过校验的格式模板，不是有效批准。** 不要把占位值替换成密码、连接字符串或业务数据。
`approval_id` 是 32 位小写十六进制标识，不是口令；三个哈希分别绑定迁移 ZIP、SQL 和备份文件。
`applied` / `pending` 必须来自所选产物与经过检查的历史快照。

```json
{
  "schema": 1,
  "purpose": "migration-execution-review",
  "approval_id": "REPLACE_WITH_32_LOWERCASE_HEX_CHARACTERS",
  "repository": "shunbin2310/Project1",
  "server": "homelab-server",
  "database": "Project1Db",
  "run_id": 123,
  "run_attempt": 1,
  "commit": "REPLACE_WITH_FULL_40_CHARACTER_COMMIT",
  "artifact_sha256": "REPLACE_WITH_MIGRATION_ZIP_HASH",
  "sql_sha256": "REPLACE_WITH_SQL_HASH",
  "applied": ["20260101000000_ExamplePreviousMigration"],
  "pending": ["20260102000000_ExampleNewMigration"],
  "approved_at": "2000-01-01T01:00:00Z",
  "expires_at": "2000-01-01T02:00:00Z",
  "backup": {
    "sha256": "REPLACE_WITH_BACKUP_FILE_HASH",
    "completed_at": "2000-01-01T00:30:00Z",
    "verified": false
  },
  "confirmations": {
    "sql_reviewed": false,
    "target_schema_reviewed": false,
    "restore_plan_ready": false
  }
}
```

## 开发者运行方式

本步骤无需在 Ubuntu 操作或读取生产密码。先运行完全离线的测试：

```powershell
python -m unittest discover -s scripts/deploy/tests -p test_migration_approval.py -v
python -m unittest discover -s scripts/deploy/tests -v
```

可选的手动校验命令格式（没有这些输入时，不必运行）：

```powershell
python scripts/deploy/migration_approval.py `
  --directory <已有的已校验迁移包目录> `
  --approval <批准记录JSON文件> `
  --status <只读历史原始JSON文件>
```

目录必须包含既有验证器生成的 `verified-migration.json` 和原始 `migrations.zip`。
每次运行先用 `load_verified()` 重新检查 ZIP digest、四文件布局、内部哈希、版本说明及迁移清单，
然后校验记录与快照。这里不访问 GitHub，不能发现远端重跑/过期；未来 CD 仍须先进行在线产物验证。
成功仅输出 `record_valid: true`、`execution_authorized: false`、`sql_executed: false`，退出码为 0；
失败退出码为 1。不提供 `--execute` 或任何生成、安装、部署选项。

已有 CI 使用 unittest discovery，会自动运行新测试，不需要修改工作流。
测试全部使用虚构 ZIP、记录和历史；主动禁止网络请求及外部进程，不打开真实数据库或任何生产 secrets。

## 不能从结果推断的事情

**任何人都可以编写 JSON，所以哈希和 `true` 不能证明管理员批准。** 备份哈希/确认只是记录声明，
本工具不访问备份文件、不核对实际 BACKUP/VERIFYONLY 结果，也不证明备份可恢复。
status 同样只是本地快照，不提供真实性证明，不能替代执行时的实时查询。
校验成功不证明 SQL 安全、结构正确、权限合适或旧应用与新结构兼容。

这一步不限制 SQL 语句种类，不能据此自动批准加列、删列或数据更新。
离线重复校验可以成功，**没有防重放/一次性批准消费功能**。
文件检查也不是 root 管理的生产授权文件信任链，不能直接把这个 CLI 加入 sudoers。

下一阶段必须单独审阅和批准：管理员控制的记录存储、身份与一次性消费、独立备份证据、
固定受限执行账号、可靠的持锁实时检查、实际执行器的临时数据库集成测试，以及中断恢复记录。
迁移锁只能协调遵守同一协议的调用；不能假设任意管理员 SQL 或应用写入也会被锁住。
代码回滚不会撤销已经提交的数据库迁移。当前没有启用这些生产操作。
