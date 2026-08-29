import type {
  CreateUnitOfMeasureRequest,
  UnitOfMeasure,
  UpdateUnitOfMeasureRequest,
} from '@/types/unitOfMeasure'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5165'

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class UnitOfMeasureApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'UnitOfMeasureApiError'
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

    throw new UnitOfMeasureApiError(response.status, message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

export const unitOfMeasureService = {
  getAll(includeInactive = false): Promise<UnitOfMeasure[]> {
    const query = includeInactive ? '?includeInactive=true' : ''
    return request<UnitOfMeasure[]>(`/api/units-of-measure${query}`)
  },

  create(payload: CreateUnitOfMeasureRequest): Promise<UnitOfMeasure> {
    return request<UnitOfMeasure>('/api/units-of-measure', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdateUnitOfMeasureRequest): Promise<UnitOfMeasure> {
    return request<UnitOfMeasure>(`/api/units-of-measure/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  deactivate(id: number): Promise<void> {
    return request<void>(`/api/units-of-measure/${id}`, { method: 'DELETE' })
  },
}
