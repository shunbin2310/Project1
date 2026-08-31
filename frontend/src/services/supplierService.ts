import type { CreateSupplierRequest, Supplier, UpdateSupplierRequest } from '@/types/supplier'
import { apiRequest, ApiError } from '@/services/apiClient'

export class SupplierApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'SupplierApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, SupplierApiError)

export const supplierService = {
  getAll(includeInactive = false): Promise<Supplier[]> {
    const query = includeInactive ? '?includeInactive=true' : ''
    return request<Supplier[]>(`/api/suppliers${query}`)
  },

  create(payload: CreateSupplierRequest): Promise<Supplier> {
    return request<Supplier>('/api/suppliers', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdateSupplierRequest): Promise<Supplier> {
    return request<Supplier>(`/api/suppliers/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  deactivate(id: number): Promise<void> {
    return request<void>(`/api/suppliers/${id}`, { method: 'DELETE' })
  },
}
