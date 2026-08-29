import type { CreateProductRequest, Product, UpdateProductRequest } from '@/types/product'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5165'

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class ProductApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ProductApiError'
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

    throw new ProductApiError(response.status, message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

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
