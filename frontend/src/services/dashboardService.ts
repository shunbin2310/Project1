import { apiRequest, ApiError } from '@/services/apiClient'
import type { Dashboard } from '@/types/dashboard'

export class DashboardApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'DashboardApiError'
  }
}

export const dashboardService = {
  get(): Promise<Dashboard> {
    return apiRequest<Dashboard>('/api/dashboard', {}, DashboardApiError)
  },
}
