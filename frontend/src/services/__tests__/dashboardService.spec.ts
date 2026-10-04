import { afterEach, describe, expect, it, vi } from 'vitest'

import { dashboardService } from '../dashboardService'

describe('dashboardService', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('loads the role-filtered dashboard', async () => {
    const payload = {
      summaryCards: [],
      reminders: [],
      recentActivity: [],
      generatedAtUtc: '2026-10-01T08:00:00Z',
    }
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify(payload), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(dashboardService.get()).resolves.toEqual(payload)
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/dashboard',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })
})
