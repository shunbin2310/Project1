import { afterEach, describe, expect, it, vi } from 'vitest'

import { purchaseOrderService } from '../purchaseOrderService'

describe('purchaseOrderService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('builds purchase order filters', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    expect(await purchaseOrderService.getAll({ supplierId: 3, status: 'Issued' })).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/purchase-orders?supplierId=3&status=Issued',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('sends the selected quotation and delivery details when creating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('{}', {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await purchaseOrderService.create({
      quotationId: 8,
      orderDate: '2026-09-16',
      expectedDeliveryDate: '2026-09-30',
      deliveryAddress: 'Main warehouse',
      notes: null,
    })

    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).toMatchObject({
      quotationId: 8,
      orderDate: '2026-09-16',
      expectedDeliveryDate: '2026-09-30',
      deliveryAddress: 'Main warehouse',
    })
  })

  it('calls the issue, cancel, and delete endpoints', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(
      async () =>
        new Response('{}', {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await purchaseOrderService.issue(5)
    await purchaseOrderService.cancel(5, 'Supplier unavailable')
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    await purchaseOrderService.delete(5)

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      'http://localhost:5165/api/purchase-orders/5/issue',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      'http://localhost:5165/api/purchase-orders/5/cancel',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ reason: 'Supplier unavailable' }),
      }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      'http://localhost:5165/api/purchase-orders/5',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })

  it('executes a purchase order workflow action with an optional comment', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('{}', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await purchaseOrderService.executeAction(5, 'REJECT', {
      comment: 'Please update the delivery address.',
    })

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/purchase-orders/5/actions/REJECT',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ comment: 'Please update the delivery address.' }),
      }),
    )
  })
})
