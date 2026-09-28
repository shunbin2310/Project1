import { afterEach, describe, expect, it, vi } from 'vitest'

import { emailRecordService } from '../emailRecordService'

describe('emailRecordService', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('builds email record filters', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await emailRecordService.getAll({
      search: ' PO-0005 ',
      status: 'Failed',
      sourceType: 'PurchaseOrder',
      createdFrom: '2026-09-01',
      createdTo: '2026-09-30',
    })

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/email-records?search=PO-0005&status=Failed&sourceType=PurchaseOrder&createdFrom=2026-09-01&createdTo=2026-09-30',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('calls details, retry, and resend endpoints', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(
      async () =>
        new Response('{}', {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await emailRecordService.getById(7)
    await emailRecordService.retry(7)
    await emailRecordService.resend(7)

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      'http://localhost:5165/api/email-records/7',
      expect.any(Object),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      'http://localhost:5165/api/email-records/7/retry',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      'http://localhost:5165/api/email-records/7/resend',
      expect.objectContaining({ method: 'POST' }),
    )
  })
})
