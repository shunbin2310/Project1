import { afterEach, describe, expect, it, vi } from 'vitest'

import { quotationService } from '../quotationService'

describe('quotationService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('builds quotation filters', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    expect(await quotationService.getAll({ purchaseRequestId: 7, status: 'Submitted' })).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/quotations?purchaseRequestId=7&status=Submitted',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('sends purchase request item prices when creating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('{}', {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await quotationService.create({
      purchaseRequestId: 7,
      supplierId: 3,
      supplierQuotationReference: 'SUP-Q-01',
      quotationDate: '2026-09-13',
      validUntil: '2026-10-13',
      notes: null,
      items: [{ purchaseRequestItemId: 11, unitPrice: 1200 }],
    })

    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).toMatchObject({
      purchaseRequestId: 7,
      supplierId: 3,
      items: [{ purchaseRequestItemId: 11, unitPrice: 1200 }],
    })
  })

  it('calls the submit and select action endpoints', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(
      async () =>
        new Response('{}', {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await quotationService.submit(8)
    await quotationService.select(8)

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      'http://localhost:5165/api/quotations/8/submit',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      'http://localhost:5165/api/quotations/8/select',
      expect.objectContaining({ method: 'POST' }),
    )
  })
})
