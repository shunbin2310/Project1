import type {
  CreateDepartmentRequest,
  Department,
  UpdateDepartmentRequest,
} from '@/types/department'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5165'

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
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

    throw new ApiError(response.status, message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

export const departmentService = {
  getAll(includeInactive = false): Promise<Department[]> {
    const query = includeInactive ? '?includeInactive=true' : ''
    return request<Department[]>(`/api/departments${query}`)
  },

  create(payload: CreateDepartmentRequest): Promise<Department> {
    return request<Department>('/api/departments', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdateDepartmentRequest): Promise<Department> {
    return request<Department>(`/api/departments/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  deactivate(id: number): Promise<void> {
    return request<void>(`/api/departments/${id}`, { method: 'DELETE' })
  },
}
