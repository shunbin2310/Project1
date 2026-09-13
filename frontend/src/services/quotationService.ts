import { apiRequest, ApiError } from '@/services/apiClient'
import type {
  CreateQuotationRequest,
  Quotation,
  QuotationComparison,
  QuotationStatus,
  UpdateQuotationRequest,
} from '@/types/quotation'

export class QuotationApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'QuotationApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, QuotationApiError)

export const quotationService = {
  getAll(filters: { purchaseRequestId?: number; status?: QuotationStatus } = {}) {
    const query = new URLSearchParams()
    if (filters.purchaseRequestId) {
      query.set('purchaseRequestId', String(filters.purchaseRequestId))
    }
    if (filters.status) query.set('status', filters.status)

    const queryString = query.toString()
    return request<Quotation[]>(`/api/quotations${queryString ? `?${queryString}` : ''}`)
  },

  getById(id: number) {
    return request<Quotation>(`/api/quotations/${id}`)
  },

  getComparison(purchaseRequestId: number) {
    return request<QuotationComparison>(
      `/api/quotations/comparison?purchaseRequestId=${purchaseRequestId}`,
    )
  },

  create(payload: CreateQuotationRequest) {
    return request<Quotation>('/api/quotations', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdateQuotationRequest) {
    return request<Quotation>(`/api/quotations/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  submit(id: number) {
    return request<Quotation>(`/api/quotations/${id}/submit`, { method: 'POST' })
  },

  select(id: number) {
    return request<Quotation>(`/api/quotations/${id}/select`, { method: 'POST' })
  },

  delete(id: number) {
    return request<void>(`/api/quotations/${id}`, { method: 'DELETE' })
  },
}
