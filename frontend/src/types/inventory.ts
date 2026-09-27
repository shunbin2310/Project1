export type InventoryTransactionType = 'GoodsReceipt' | 'AdjustmentIncrease' | 'AdjustmentDecrease'

export type InventoryStockStatus = 'Healthy' | 'LowStock' | 'OutOfStock'

export interface InventoryBalance {
  productId: number
  productCode: string
  productName: string
  productCategoryId: number
  productCategoryCode: string
  productCategoryName: string
  unitOfMeasureCode: string
  unitOfMeasureName: string
  quantityOnHand: number
  reorderLevel: number
  isLowStock: boolean
  isProductActive: boolean
  lastUpdatedAtUtc: string | null
}

export interface InventoryTransaction {
  id: number
  productId: number
  productCode: string
  productName: string
  unitOfMeasureCode: string
  type: InventoryTransactionType
  quantityChange: number
  quantityBefore: number
  quantityAfter: number
  referenceType: string
  referenceId: number
  referenceNumber: string
  goodsReceiptItemId: number | null
  performedByUserId: number
  performedByName: string
  occurredAtUtc: string
}

export interface InventoryBalanceFilters {
  productCategoryId?: number
  lowStock?: boolean
  includeInactive?: boolean
  search?: string
}

export interface InventoryTransactionFilters {
  type?: InventoryTransactionType
  dateFrom?: string
  dateTo?: string
}
