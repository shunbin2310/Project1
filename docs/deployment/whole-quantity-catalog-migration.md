# 整数数量与分类、单位移除

`RemoveProductCategoryAndUnitEnforceWholeQuantities` migration 删除分类和单位表、产品关联字段，以及报价、采购单、收货和库存流水中的单位字段。产品、申请、订单、收货、库存记录和金额保留。

数量仍使用现有 decimal 存储范围，新增 CHECK 约束只允许整数，避免数据库隐式取整。单价和金额继续保留小数。申请草稿和补货数量允许 0；提交申请、实际收货数量必须大于 0。

发布前备份数据库并验证恢复方法。迁移不可通过 Down 恢复：重新创建空分类、单位表无法恢复原值或关联，回退需要恢复发布前的完整备份和对应旧应用版本。

迁移首先检查全部数量列。如发现旧小数数量，会抛出 `51000`，指出所在表，并在删除分类、单位前终止。不要直接重跑部署或统一四舍五入；先核对相关申请、订单、收货和库存流水，确定相互一致的修正方案。

可在发布前执行以下只读检查。查询无结果时，现有数量符合整数规则：

```sql
SELECT 'Products' AS TableName, Id, 'ReorderLevel' AS ColumnName, ReorderLevel AS Quantity
FROM dbo.Products WHERE ReorderLevel <> ROUND(ReorderLevel, 0)
UNION ALL
SELECT 'PurchaseRequestItems', Id, 'Quantity', Quantity
FROM dbo.PurchaseRequestItems WHERE Quantity <> ROUND(Quantity, 0)
UNION ALL
SELECT 'QuotationItems', Id, 'Quantity', Quantity
FROM dbo.QuotationItems WHERE Quantity <> ROUND(Quantity, 0)
UNION ALL
SELECT 'PurchaseOrderItems', Id, 'Quantity', Quantity
FROM dbo.PurchaseOrderItems WHERE Quantity <> ROUND(Quantity, 0)
UNION ALL
SELECT 'GoodsReceiptItems', Id, 'OrderedQuantity', OrderedQuantity
FROM dbo.GoodsReceiptItems WHERE OrderedQuantity <> ROUND(OrderedQuantity, 0)
UNION ALL
SELECT 'GoodsReceiptItems', Id, 'QuantityReceived', QuantityReceived
FROM dbo.GoodsReceiptItems WHERE QuantityReceived <> ROUND(QuantityReceived, 0)
UNION ALL
SELECT 'InventoryBalances', Id, 'QuantityOnHand', QuantityOnHand
FROM dbo.InventoryBalances WHERE QuantityOnHand <> ROUND(QuantityOnHand, 0)
UNION ALL
SELECT 'InventoryTransactions', Id, 'QuantityChange', QuantityChange
FROM dbo.InventoryTransactions WHERE QuantityChange <> ROUND(QuantityChange, 0)
UNION ALL
SELECT 'InventoryTransactions', Id, 'QuantityBefore', QuantityBefore
FROM dbo.InventoryTransactions WHERE QuantityBefore <> ROUND(QuantityBefore, 0)
UNION ALL
SELECT 'InventoryTransactions', Id, 'QuantityAfter', QuantityAfter
FROM dbo.InventoryTransactions WHERE QuantityAfter <> ROUND(QuantityAfter, 0);
```

代码和数据库必须一起升级。生成迁移不修改数据库；现有启动迁移流程在启用时应用它。此次修改未自动执行本地或生产数据库迁移。

历史 `AddInventoryLedger` 的回填 SQL 仅增加动态执行包装，原 SQL 和迁移记录不变。这使完整 idempotent 脚本在单位列删除后仍能重复执行，避免 SQL Server 提前编译已跳过的历史回填语句。
