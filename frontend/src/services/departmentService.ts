import type {
  CreateDepartmentRequest,
  Department,
  UpdateDepartmentRequest,
} from '@/types/department'
import { apiRequest, ApiError as BaseApiError } from '@/services/apiClient'

export class ApiError extends BaseApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'ApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) => apiRequest<T>(path, options, ApiError)

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
