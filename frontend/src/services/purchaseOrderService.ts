import { apiRequest, ApiError } from '@/services/apiClient'
import type {
  CreatePurchaseOrderRequest,
  PurchaseOrder,
  PurchaseOrderEmailPreview,
  PurchaseOrderStatus,
  UpdatePurchaseOrderRequest,
} from '@/types/purchaseOrder'

export class PurchaseOrderApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'PurchaseOrderApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, PurchaseOrderApiError)

export const purchaseOrderService = {
  getAll(filters: { supplierId?: number; status?: PurchaseOrderStatus } = {}) {
    const query = new URLSearchParams()
    if (filters.supplierId) query.set('supplierId', String(filters.supplierId))
    if (filters.status) query.set('status', filters.status)

    const queryString = query.toString()
    return request<PurchaseOrder[]>(`/api/purchase-orders${queryString ? `?${queryString}` : ''}`)
  },

  getById(id: number) {
    return request<PurchaseOrder>(`/api/purchase-orders/${id}`)
  },

  create(payload: CreatePurchaseOrderRequest) {
    return request<PurchaseOrder>('/api/purchase-orders', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdatePurchaseOrderRequest) {
    return request<PurchaseOrder>(`/api/purchase-orders/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  issue(id: number) {
    return request<PurchaseOrder>(`/api/purchase-orders/${id}/issue`, { method: 'POST' })
  },

  getEmailPreview(id: number) {
    return request<PurchaseOrderEmailPreview>(`/api/purchase-orders/${id}/email-preview`)
  },

  retryEmail(id: number) {
    return request<PurchaseOrder>(`/api/purchase-orders/${id}/email-retry`, { method: 'POST' })
  },

  cancel(id: number, reason: string) {
    return request<PurchaseOrder>(`/api/purchase-orders/${id}/cancel`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason }),
    })
  },

  delete(id: number) {
    return request<void>(`/api/purchase-orders/${id}`, { method: 'DELETE' })
  },
}
