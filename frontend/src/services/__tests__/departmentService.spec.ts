import { afterEach, describe, expect, it, vi } from 'vitest'

import { departmentService } from '../departmentService'

describe('departmentService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests inactive departments when requested', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await departmentService.getAll(true)

    expect(result).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/departments?includeInactive=true',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('returns the API conflict message for duplicate codes', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(
        new Response(
          JSON.stringify({
            title: 'Department code already exists.',
            detail: "A department with code 'IT' already exists.",
          }),
          {
            status: 409,
            headers: { 'Content-Type': 'application/json' },
          },
        ),
      ),
    )

    const request = departmentService.create({
      code: 'IT',
      name: 'Information Technology',
      description: null,
    })

    await expect(request).rejects.toMatchObject({
      name: 'ApiError',
      status: 409,
      message: "A department with code 'IT' already exists.",
    })
  })

  it('does not send a department code when updating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 1,
          code: 'IT',
          name: 'Technology',
          description: null,
          isActive: true,
          createdAtUtc: '2026-08-25T00:00:00Z',
          updatedAtUtc: '2026-08-25T01:00:00Z',
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await departmentService.update(1, {
      name: 'Technology',
      description: null,
      isActive: true,
    })

    const requestOptions = fetchMock.mock.calls[0]?.[1]
    const body = JSON.parse(String(requestOptions?.body)) as Record<string, unknown>

    expect(body).toEqual({
      name: 'Technology',
      description: null,
      isActive: true,
    })
    expect(body).not.toHaveProperty('code')
  })
})
