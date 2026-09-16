import type { PurchaseOrder, PurchaseOrderItem } from '@/types/purchaseOrder'

export type GoodsReceiptStatus = 'Draft' | 'Posted'

export interface GoodsReceiptItem {
  id: number
  purchaseOrderItemId: number
  productId: number
  productCode: string
  productName: string
  unitOfMeasureCode: string
  orderedQuantity: number
  quantityReceived: number
}

export interface GoodsReceipt {
  id: number
  goodsReceiptNumber: string
  purchaseOrderId: number
  purchaseOrderNumber: string
  supplierId: number
  supplierCode: string
  supplierName: string
  supplierDeliveryNoteNumber: string | null
  receivedDate: string
  notes: string | null
  status: GoodsReceiptStatus
  createdByUserId: number
  createdByName: string
  createdAtUtc: string
  updatedAtUtc: string | null
  postedAtUtc: string | null
  postedByUserId: number | null
  postedByName: string | null
  items: GoodsReceiptItem[]
}

export interface GoodsReceiptItemInput {
  purchaseOrderItemId: number
  quantityReceived: number
}

export interface CreateGoodsReceiptRequest {
  purchaseOrderId: number
  supplierDeliveryNoteNumber: string | null
  receivedDate: string
  notes: string | null
  items: GoodsReceiptItemInput[]
}

export type UpdateGoodsReceiptRequest = Omit<CreateGoodsReceiptRequest, 'purchaseOrderId'>

export type GoodsReceiptFormValues = CreateGoodsReceiptRequest

export interface ReceivablePurchaseOrderItem extends PurchaseOrderItem {
  previouslyReceivedQuantity: number
  remainingQuantity: number
}

export interface ReceivablePurchaseOrder extends Omit<PurchaseOrder, 'items'> {
  items: ReceivablePurchaseOrderItem[]
}
