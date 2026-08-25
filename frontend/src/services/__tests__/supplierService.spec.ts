import { afterEach, describe, expect, it, vi } from 'vitest'

import { supplierService } from '../supplierService'

describe('supplierService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests inactive suppliers when requested', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await supplierService.getAll(true)

    expect(result).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/suppliers?includeInactive=true',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('does not send a supplier code when creating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 1,
          code: 'SUP-0001',
          name: 'Example Supplies',
          contactPerson: null,
          email: null,
          phone: null,
          address: null,
          isActive: true,
          createdAtUtc: '2026-08-25T00:00:00Z',
          updatedAtUtc: null,
        }),
        {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await supplierService.create({
      name: 'Example Supplies',
      contactPerson: null,
      email: null,
      phone: null,
      address: null,
    })

    const requestOptions = fetchMock.mock.calls[0]?.[1]
    const body = JSON.parse(String(requestOptions?.body)) as Record<string, unknown>

    expect(body).toEqual({
      name: 'Example Supplies',
      contactPerson: null,
      email: null,
      phone: null,
      address: null,
    })
    expect(body).not.toHaveProperty('code')
  })

  it('does not send a supplier code when updating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 1,
          code: 'SUP-001',
          name: 'Example Supplies Malaysia',
          contactPerson: null,
          email: null,
          phone: null,
          address: null,
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

    await supplierService.update(1, {
      name: 'Example Supplies Malaysia',
      contactPerson: null,
      email: null,
      phone: null,
      address: null,
      isActive: true,
    })

    const requestOptions = fetchMock.mock.calls[0]?.[1]
    const body = JSON.parse(String(requestOptions?.body)) as Record<string, unknown>

    expect(body).toEqual({
      name: 'Example Supplies Malaysia',
      contactPerson: null,
      email: null,
      phone: null,
      address: null,
      isActive: true,
    })
    expect(body).not.toHaveProperty('code')
  })
})
