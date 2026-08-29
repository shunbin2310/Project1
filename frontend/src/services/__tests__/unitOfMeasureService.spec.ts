import { afterEach, describe, expect, it, vi } from 'vitest'

import { unitOfMeasureService } from '../unitOfMeasureService'

describe('unitOfMeasureService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests inactive units when requested', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    expect(await unitOfMeasureService.getAll(true)).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/units-of-measure?includeInactive=true',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('sends the unit code when creating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 1,
          code: 'PCS',
          name: 'Pieces',
          description: null,
          isActive: true,
          createdAtUtc: '2026-08-29T00:00:00Z',
          updatedAtUtc: null,
        }),
        { status: 201, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await unitOfMeasureService.create({ code: 'PCS', name: 'Pieces', description: null })

    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).toEqual({ code: 'PCS', name: 'Pieces', description: null })
  })

  it('does not send the unit code when updating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 1,
          code: 'PCS',
          name: 'Piece',
          description: null,
          isActive: true,
          createdAtUtc: '2026-08-29T00:00:00Z',
          updatedAtUtc: '2026-08-29T01:00:00Z',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await unitOfMeasureService.update(1, {
      name: 'Piece',
      description: null,
      isActive: true,
    })

    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).not.toHaveProperty('code')
  })

  it('uses the conflict detail returned by the API', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          detail: "A unit of measure with code 'PCS' already exists.",
        }),
        { status: 409, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const request = unitOfMeasureService.create({
      code: 'PCS',
      name: 'Pieces',
      description: null,
    })

    await expect(request).rejects.toMatchObject({
      status: 409,
      message: "A unit of measure with code 'PCS' already exists.",
    })
  })
})
