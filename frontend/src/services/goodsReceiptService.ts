import { apiRequest, ApiError } from '@/services/apiClient'
import type {
  CreateGoodsReceiptRequest,
  GoodsReceipt,
  GoodsReceiptStatus,
  UpdateGoodsReceiptRequest,
} from '@/types/goodsReceipt'

export class GoodsReceiptApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'GoodsReceiptApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, GoodsReceiptApiError)

export const goodsReceiptService = {
  getAll(
    filters: {
      purchaseOrderId?: number
      supplierId?: number
      status?: GoodsReceiptStatus
    } = {},
  ) {
    const query = new URLSearchParams()
    if (filters.purchaseOrderId) {
      query.set('purchaseOrderId', String(filters.purchaseOrderId))
    }
    if (filters.supplierId) query.set('supplierId', String(filters.supplierId))
    if (filters.status) query.set('status', filters.status)

    const queryString = query.toString()
    return request<GoodsReceipt[]>(`/api/goods-receipts${queryString ? `?${queryString}` : ''}`)
  },

  getById(id: number) {
    return request<GoodsReceipt>(`/api/goods-receipts/${id}`)
  },

  create(payload: CreateGoodsReceiptRequest) {
    return request<GoodsReceipt>('/api/goods-receipts', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdateGoodsReceiptRequest) {
    return request<GoodsReceipt>(`/api/goods-receipts/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  post(id: number) {
    return request<GoodsReceipt>(`/api/goods-receipts/${id}/post`, { method: 'POST' })
  },

  delete(id: number) {
    return request<void>(`/api/goods-receipts/${id}`, { method: 'DELETE' })
  },
}
