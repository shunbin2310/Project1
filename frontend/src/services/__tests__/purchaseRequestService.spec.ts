import { afterEach, describe, expect, it, vi } from 'vitest'

import { purchaseRequestService } from '../purchaseRequestService'

describe('purchaseRequestService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('filters requests by workflow step', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    expect(await purchaseRequestService.getAll('FINANCE_REVIEW')).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/purchase-requests?stepCode=FINANCE_REVIEW',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('sends only the comment when executing an action', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('{}', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await purchaseRequestService.executeAction(7, 'APPROVE', {
      comment: 'Budget confirmed.',
    })

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/purchase-requests/7/actions/APPROVE',
      expect.objectContaining({ method: 'POST' }),
    )
    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).toEqual({ comment: 'Budget confirmed.' })
  })

  it('surfaces workflow authorization errors', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify({ detail: 'The actor is not authorized.' }), {
        status: 403,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const action = purchaseRequestService.executeAction(7, 'APPROVE', {
      comment: null,
    })

    await expect(action).rejects.toMatchObject({
      status: 403,
      message: 'The actor is not authorized.',
    })
  })
})
