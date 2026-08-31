import type {
  CreateProductCategoryRequest,
  ProductCategory,
  UpdateProductCategoryRequest,
} from '@/types/productCategory'
import { apiRequest, ApiError } from '@/services/apiClient'

export class ProductCategoryApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'ProductCategoryApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, ProductCategoryApiError)

export const productCategoryService = {
  getAll(includeInactive = false): Promise<ProductCategory[]> {
    const query = includeInactive ? '?includeInactive=true' : ''
    return request<ProductCategory[]>(`/api/product-categories${query}`)
  },

  create(payload: CreateProductCategoryRequest): Promise<ProductCategory> {
    return request<ProductCategory>('/api/product-categories', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdateProductCategoryRequest): Promise<ProductCategory> {
    return request<ProductCategory>(`/api/product-categories/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  deactivate(id: number): Promise<void> {
    return request<void>(`/api/product-categories/${id}`, { method: 'DELETE' })
  },
}
