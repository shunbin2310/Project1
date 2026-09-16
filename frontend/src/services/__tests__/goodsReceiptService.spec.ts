import { afterEach, describe, expect, it, vi } from 'vitest'

import { goodsReceiptService } from '../goodsReceiptService'

describe('goodsReceiptService', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('builds goods receipt filters', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    expect(
      await goodsReceiptService.getAll({ purchaseOrderId: 5, supplierId: 2, status: 'Posted' }),
    ).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/goods-receipts?purchaseOrderId=5&supplierId=2&status=Posted',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('sends receipt header and item quantities when creating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('{}', {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await goodsReceiptService.create({
      purchaseOrderId: 5,
      supplierDeliveryNoteNumber: 'DN-001',
      receivedDate: '2026-09-16',
      notes: 'Boxes checked.',
      items: [{ purchaseOrderItemId: 2, quantityReceived: 2 }],
    })

    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).toMatchObject({
      purchaseOrderId: 5,
      supplierDeliveryNoteNumber: 'DN-001',
      items: [{ purchaseOrderItemId: 2, quantityReceived: 2 }],
    })
  })

  it('calls the post and delete endpoints', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('{}', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await goodsReceiptService.post(9)
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    await goodsReceiptService.delete(9)

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      'http://localhost:5165/api/goods-receipts/9/post',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      'http://localhost:5165/api/goods-receipts/9',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
