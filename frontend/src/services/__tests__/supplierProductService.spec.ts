import { afterEach, describe, expect, it, vi } from 'vitest'

import { supplierProductService } from '../supplierProductService'

describe('supplierProductService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('builds supplier-product query filters', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await supplierProductService.getAll({
      supplierId: 2,
      productId: 5,
      includeInactive: true,
    })

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/supplier-products?supplierId=2&productId=5&includeInactive=true',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('sends only relationship creation fields', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('{}', {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await supplierProductService.create({
      supplierId: 1,
      productId: 2,
      isPreferred: true,
    })

    const requestOptions = fetchMock.mock.calls[0]?.[1]
    expect(JSON.parse(String(requestOptions?.body))).toEqual({
      supplierId: 1,
      productId: 2,
      isPreferred: true,
    })
  })

  it('uses DELETE for relationship removal', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await supplierProductService.delete(7)

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/supplier-products/7',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
