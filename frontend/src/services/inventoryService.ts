import { apiRequest, ApiError } from '@/services/apiClient'
import type {
  InventoryBalance,
  InventoryBalanceFilters,
  InventoryTransaction,
  InventoryTransactionFilters,
} from '@/types/inventory'

export class InventoryApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'InventoryApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, InventoryApiError)

export const inventoryService = {
  getAll(filters: InventoryBalanceFilters = {}) {
    const query = new URLSearchParams()
    if (filters.productCategoryId) {
      query.set('productCategoryId', String(filters.productCategoryId))
    }
    if (filters.lowStock !== undefined) query.set('lowStock', String(filters.lowStock))
    if (filters.includeInactive) query.set('includeInactive', 'true')
    if (filters.search?.trim()) query.set('search', filters.search.trim())

    const queryString = query.toString()
    return request<InventoryBalance[]>(`/api/inventory${queryString ? `?${queryString}` : ''}`)
  },

  getByProductId(productId: number) {
    return request<InventoryBalance>(`/api/inventory/${productId}`)
  },

  getTransactions(productId: number, filters: InventoryTransactionFilters = {}) {
    const query = new URLSearchParams()
    if (filters.type) query.set('type', filters.type)
    if (filters.dateFrom) query.set('dateFrom', filters.dateFrom)
    if (filters.dateTo) query.set('dateTo', filters.dateTo)

    const queryString = query.toString()
    return request<InventoryTransaction[]>(
      `/api/inventory/${productId}/transactions${queryString ? `?${queryString}` : ''}`,
    )
  },
}
