import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

import { authSessionStorageKey } from '@/services/authSession'
import type { LoginRequest, LoginResponse } from '@/types/auth'
import { useAuthStore } from '../auth'

const mocks = vi.hoisted(() => ({
  login: vi.fn<(request: LoginRequest) => Promise<LoginResponse>>(),
}))

vi.mock('@/services/authService', () => ({
  authService: { login: mocks.login },
}))

const loginResponse = {
  accessToken: 'signed-test-token',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  user: {
    id: 4,
    email: 'admin@demo.local',
    fullName: 'Demo Admin',
    departmentId: 1,
    departmentCode: 'IT',
    departmentName: 'Information Technology',
    roles: ['ADMIN'],
  },
}

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mocks.login.mockResolvedValue(loginResponse)
  })

  afterEach(() => {
    window.sessionStorage.clear()
    vi.clearAllMocks()
  })

  it('stores the authenticated session in sessionStorage', async () => {
    const store = useAuthStore()

    await store.login({ email: 'admin@demo.local', password: 'password' })

    expect(store.isAuthenticated).toBe(true)
    expect(store.user?.fullName).toBe('Demo Admin')
    expect(JSON.parse(window.sessionStorage.getItem(authSessionStorageKey) ?? '{}')).toEqual(
      loginResponse,
    )
  })

  it('clears the stored session during logout', async () => {
    const store = useAuthStore()
    await store.login({ email: 'admin@demo.local', password: 'password' })

    store.logout()

    expect(store.isAuthenticated).toBe(false)
    expect(window.sessionStorage.getItem(authSessionStorageKey)).toBeNull()
  })
})
