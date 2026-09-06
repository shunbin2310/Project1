import { afterEach, describe, expect, it, vi } from 'vitest'

import { userService } from '../userService'

describe('userService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests inactive users and search text', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await userService.getAll(true, 'Alex Tan')

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/users?includeInactive=true&search=Alex+Tan',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('does not send email or password when updating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 5,
          email: 'user@demo.local',
          fullName: 'Updated User',
          departmentId: null,
          departmentCode: null,
          departmentName: null,
          isActive: true,
          roles: ['REQUESTER'],
          createdAtUtc: '2026-08-31T00:00:00Z',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await userService.update(5, {
      fullName: 'Updated User',
      departmentId: null,
      roles: ['REQUESTER'],
    })

    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).toEqual({
      fullName: 'Updated User',
      departmentId: null,
      roles: ['REQUESTER'],
    })
    expect(body).not.toHaveProperty('email')
    expect(body).not.toHaveProperty('temporaryPassword')
  })

  it('sends an explicit active status', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 5,
          email: 'user@demo.local',
          fullName: 'User',
          departmentId: null,
          departmentCode: null,
          departmentName: null,
          isActive: false,
          roles: ['REQUESTER'],
          createdAtUtc: '2026-08-31T00:00:00Z',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await userService.setActive(5, false)

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/users/5/status',
      expect.objectContaining({ method: 'PATCH', body: JSON.stringify({ isActive: false }) }),
    )
  })
})
