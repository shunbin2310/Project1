import type {
  CreateProductCategoryRequest,
  ProductCategory,
  UpdateProductCategoryRequest,
} from '@/types/productCategory'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5165'

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class ProductCategoryApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ProductCategoryApiError'
  }
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...options,
    headers: {
      Accept: 'application/json',
      ...options?.headers,
    },
  })

  if (!response.ok) {
    let message = `Request failed with status ${response.status}`

    try {
      const problem = (await response.json()) as ProblemDetails
      const validationMessage = problem.errors
        ? Object.values(problem.errors).flat().join(' ')
        : undefined

      message = validationMessage || problem.detail || problem.title || message
    } catch {
      // The response did not include a JSON error body.
    }

    throw new ProductCategoryApiError(response.status, message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

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
