export type PurchaseOrderStatus =
  | 'Draft'
  | 'Issued'
  | 'PartiallyReceived'
  | 'Received'
  | 'Cancelled'

export type EmailDeliveryStatus = 'Pending' | 'Sent' | 'Failed'

export interface PurchaseOrderEmailDelivery {
  id: number
  recipientEmail: string
  subject: string
  status: EmailDeliveryStatus
  attemptCount: number
  createdAtUtc: string
  lastAttemptAtUtc: string | null
  sentAtUtc: string | null
  lastError: string | null
}

export interface PurchaseOrderEmailPreview {
  id: number
  recipientEmail: string
  subject: string
  htmlBody: string
  status: EmailDeliveryStatus
}

export interface PurchaseOrderItem {
  id: number
  quotationItemId: number
  productId: number
  productCode: string
  productName: string
  unitOfMeasureCode: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

export interface PurchaseOrder {
  id: number
  purchaseOrderNumber: string
  quotationId: number
  quotationNumber: string
  purchaseRequestId: number
  purchaseRequestNumber: string
  supplierId: number
  supplierCode: string
  supplierName: string
  supplierQuotationReference: string | null
  orderDate: string
  expectedDeliveryDate: string | null
  deliveryAddress: string | null
  notes: string | null
  status: PurchaseOrderStatus
  totalAmount: number
  createdByUserId: number
  createdByName: string
  createdAtUtc: string
  updatedAtUtc: string | null
  issuedAtUtc: string | null
  issuedByUserId: number | null
  issuedByName: string | null
  cancelledAtUtc: string | null
  cancelledByUserId: number | null
  cancelledByName: string | null
  cancellationReason: string | null
  emailDelivery: PurchaseOrderEmailDelivery | null
  items: PurchaseOrderItem[]
}

export interface CreatePurchaseOrderRequest {
  quotationId: number
  orderDate: string
  expectedDeliveryDate: string | null
  deliveryAddress: string | null
  notes: string | null
}

export type UpdatePurchaseOrderRequest = Omit<CreatePurchaseOrderRequest, 'quotationId'>

export type PurchaseOrderFormValues = CreatePurchaseOrderRequest
