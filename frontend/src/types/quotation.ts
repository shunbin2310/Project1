export type QuotationStatus = 'Draft' | 'Submitted' | 'Selected' | 'NotSelected'

export interface QuotationItem {
  id: number
  purchaseRequestItemId: number
  productId: number
  productCode: string
  productName: string
  unitOfMeasureCode: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

export interface Quotation {
  id: number
  quotationNumber: string
  purchaseRequestId: number
  purchaseRequestNumber: string
  supplierId: number
  supplierCode: string
  supplierName: string
  supplierQuotationReference: string | null
  quotationDate: string
  validUntil: string | null
  notes: string | null
  status: QuotationStatus
  totalAmount: number
  createdByUserId: number
  createdByName: string
  createdAtUtc: string
  updatedAtUtc: string | null
  submittedAtUtc: string | null
  selectedAtUtc: string | null
  items: QuotationItem[]
}

export interface QuotationItemPriceInput {
  purchaseRequestItemId: number
  unitPrice: number
}

export interface CreateQuotationRequest {
  purchaseRequestId: number
  supplierId: number
  supplierQuotationReference: string | null
  quotationDate: string
  validUntil: string | null
  notes: string | null
  items: QuotationItemPriceInput[]
}

export type UpdateQuotationRequest = Omit<
  CreateQuotationRequest,
  'purchaseRequestId' | 'supplierId'
>

export type QuotationFormValues = CreateQuotationRequest

export interface QuotationComparisonEntry {
  quotationId: number
  quotationNumber: string
  supplierId: number
  supplierCode: string
  supplierName: string
  supplierQuotationReference: string | null
  quotationDate: string
  validUntil: string | null
  status: QuotationStatus
  totalAmount: number
  isLowestTotal: boolean
  isSelected: boolean
  items: QuotationItem[]
}

export interface QuotationComparison {
  purchaseRequestId: number
  purchaseRequestNumber: string
  selectedQuotationId: number | null
  lowestTotalAmount: number | null
  quotations: QuotationComparisonEntry[]
}
