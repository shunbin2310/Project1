import type {
  CreatePurchaseRequestRequest,
  PurchaseRequest,
  PurchaseRequestActionRequest,
  UpdatePurchaseRequestRequest,
} from '@/types/purchaseRequest'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5165'

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class PurchaseRequestApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'PurchaseRequestApiError'
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

    throw new PurchaseRequestApiError(response.status, message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

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
