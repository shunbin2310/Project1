import { apiRequest, ApiError as BaseApiError } from '@/services/apiClient'
import type { ApplicationRole } from '@/types/auth'
import type { CreateUserRequest, UpdateUserRequest, User } from '@/types/user'

export class ApiError extends BaseApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'ApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) => apiRequest<T>(path, options, ApiError)

export const userService = {
  getAll(includeInactive = false, search = ''): Promise<User[]> {
    const parameters = new URLSearchParams()
    if (includeInactive) parameters.set('includeInactive', 'true')
    if (search.trim()) parameters.set('search', search.trim())
    const query = parameters.size ? `?${parameters.toString()}` : ''
    return request<User[]>(`/api/users${query}`)
  },

  getRoles(): Promise<ApplicationRole[]> {
    return request<ApplicationRole[]>('/api/users/roles')
  },

  create(payload: CreateUserRequest): Promise<User> {
    return request<User>('/api/users', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  update(id: number, payload: UpdateUserRequest): Promise<User> {
    return request<User>(`/api/users/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  setActive(id: number, isActive: boolean): Promise<User> {
    return request<User>(`/api/users/${id}/status`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ isActive }),
    })
  },
}
