import type {
  CreateUnitOfMeasureRequest,
  UnitOfMeasure,
  UpdateUnitOfMeasureRequest,
} from '@/types/unitOfMeasure'
import { apiRequest, ApiError } from '@/services/apiClient'

export class UnitOfMeasureApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'UnitOfMeasureApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, UnitOfMeasureApiError)

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
