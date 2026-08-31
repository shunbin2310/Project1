import { apiRequest, ApiError } from '@/services/apiClient'
import type { AuthenticatedUser, LoginRequest, LoginResponse } from '@/types/auth'

export class AuthenticationApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'AuthenticationApiError'
  }
}

export const authService = {
  login(payload: LoginRequest): Promise<LoginResponse> {
    return apiRequest<LoginResponse>(
      '/api/auth/login',
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
        authenticated: false,
        handleUnauthorized: false,
      },
      AuthenticationApiError,
    )
  },

  getCurrentUser(): Promise<AuthenticatedUser> {
    return apiRequest<AuthenticatedUser>('/api/auth/me', {}, AuthenticationApiError)
  },
}
