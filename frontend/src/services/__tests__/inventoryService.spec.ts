import { afterEach, describe, expect, it, vi } from 'vitest'

import { inventoryService } from '../inventoryService'

describe('inventoryService', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('builds inventory balance filters', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    expect(
      await inventoryService.getAll({
        productCategoryId: 2,
        lowStock: true,
        includeInactive: true,
        search: ' chair ',
      }),
    ).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/inventory?productCategoryId=2&lowStock=true&includeInactive=true&search=chair',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('gets one product balance', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('{}', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await inventoryService.getByProductId(3)

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/inventory/3',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('builds transaction history filters', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await inventoryService.getTransactions(3, {
      type: 'GoodsReceipt',
      dateFrom: '2026-09-01',
      dateTo: '2026-09-30',
    })

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/inventory/3/transactions?type=GoodsReceipt&dateFrom=2026-09-01&dateTo=2026-09-30',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })
})
