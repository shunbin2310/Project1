import { afterEach, describe, expect, it, vi } from 'vitest'

import { setUnauthorizedHandler } from '@/services/apiClient'
import { authSessionStorageKey } from '@/services/authSession'
import { authService } from '../authService'

const session = {
  accessToken: 'signed-test-token',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  user: {
    id: 1,
    email: 'requester@demo.local',
    fullName: 'Demo Requester',
    departmentId: 1,
    departmentCode: 'IT',
    departmentName: 'Information Technology',
    roles: ['REQUESTER'],
  },
}

describe('authService and API authentication', () => {
  afterEach(() => {
    window.sessionStorage.clear()
    setUnauthorizedHandler(null)
    vi.unstubAllGlobals()
  })

  it('does not attach an old token to the login request', async () => {
    window.sessionStorage.setItem(authSessionStorageKey, JSON.stringify(session))
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify(session), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await authService.login({ email: session.user.email, password: 'password' })

    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Record<string, string>
    expect(headers.Authorization).toBeUndefined()
  })

  it('attaches the JWT to protected requests', async () => {
    window.sessionStorage.setItem(authSessionStorageKey, JSON.stringify(session))
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify(session.user), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await authService.getCurrentUser()

    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Record<string, string>
    expect(headers.Authorization).toBe('Bearer signed-test-token')
  })

  it('runs the centralized unauthorized handler for a protected 401 response', async () => {
    window.sessionStorage.setItem(authSessionStorageKey, JSON.stringify(session))
    const unauthorized = vi.fn<() => void>()
    setUnauthorizedHandler(unauthorized)
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 401 })),
    )

    await expect(authService.getCurrentUser()).rejects.toMatchObject({ status: 401 })
    expect(unauthorized).toHaveBeenCalledOnce()
  })
})
