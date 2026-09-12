import { apiRequest, ApiError } from '@/services/apiClient'
import type {
  CreateSupplierProductRequest,
  SupplierProduct,
  SupplierProductFilters,
  UpdateSupplierProductRequest,
} from '@/types/supplierProduct'

export class SupplierProductApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'SupplierProductApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, SupplierProductApiError)

export const supplierProductService = {
  getAll(filters: SupplierProductFilters = {}): Promise<SupplierProduct[]> {
    const query = new URLSearchParams()

    if (filters.supplierId) query.set('supplierId', String(filters.supplierId))
    if (filters.productId) query.set('productId', String(filters.productId))
    if (filters.includeInactive) query.set('includeInactive', 'true')

    const queryString = query.toString()
    return request<SupplierProduct[]>(
      `/api/supplier-products${queryString ? `?${queryString}` : ''}`,
    )
  },

  create(payload: CreateSupplierProductRequest): Promise<SupplierProduct> {
    return request<SupplierProduct>('/api/supplier-products', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdateSupplierProductRequest): Promise<SupplierProduct> {
    return request<SupplierProduct>(`/api/supplier-products/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  delete(id: number): Promise<void> {
    return request<void>(`/api/supplier-products/${id}`, { method: 'DELETE' })
  },
}
