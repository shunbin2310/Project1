import type { CreateProductRequest, Product, UpdateProductRequest } from '@/types/product'
import { apiRequest, ApiError } from '@/services/apiClient'

export class ProductApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'ProductApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, ProductApiError)

export const productService = {
  getAll(includeInactive = false): Promise<Product[]> {
    const query = includeInactive ? '?includeInactive=true' : ''
    return request<Product[]>(`/api/products${query}`)
  },

  create(payload: CreateProductRequest): Promise<Product> {
    return request<Product>('/api/products', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdateProductRequest): Promise<Product> {
    return request<Product>(`/api/products/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  deactivate(id: number): Promise<void> {
    return request<void>(`/api/products/${id}`, { method: 'DELETE' })
  },
}
