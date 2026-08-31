import type {
  CreatePurchaseRequestRequest,
  PurchaseRequest,
  PurchaseRequestActionRequest,
  UpdatePurchaseRequestRequest,
} from '@/types/purchaseRequest'
import { apiRequest, ApiError } from '@/services/apiClient'

export class PurchaseRequestApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'PurchaseRequestApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, PurchaseRequestApiError)

export const purchaseRequestService = {
  getAll(stepCode?: string): Promise<PurchaseRequest[]> {
    const query = stepCode ? `?stepCode=${encodeURIComponent(stepCode)}` : ''
    return request<PurchaseRequest[]>(`/api/purchase-requests${query}`)
  },

  getById(id: number): Promise<PurchaseRequest> {
    return request<PurchaseRequest>(`/api/purchase-requests/${id}`)
  },

  create(payload: CreatePurchaseRequestRequest): Promise<PurchaseRequest> {
    return request<PurchaseRequest>('/api/purchase-requests', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdatePurchaseRequestRequest): Promise<PurchaseRequest> {
    return request<PurchaseRequest>(`/api/purchase-requests/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  delete(id: number): Promise<void> {
    return request<void>(`/api/purchase-requests/${id}`, { method: 'DELETE' })
  },

  executeAction(
    id: number,
    actionCode: string,
    payload: PurchaseRequestActionRequest,
  ): Promise<PurchaseRequest> {
    return request<PurchaseRequest>(
      `/api/purchase-requests/${id}/actions/${encodeURIComponent(actionCode)}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      },
    )
  },
}
