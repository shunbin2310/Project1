import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

describe('apiClient deployment addresses', () => {
  beforeEach(() => {
    vi.resetModules()
    window.sessionStorage.clear()
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
  })

  it.each([
    { name: 'production same-origin', baseUrl: '', expectedPrefix: '' },
    {
      name: 'local development fallback',
      baseUrl: undefined,
      expectedPrefix: 'http://localhost:5165',
    },
    {
      name: 'explicit API address',
      baseUrl: 'https://api.example.test',
      expectedPrefix: 'https://api.example.test',
    },
  ])('uses $name for JSON requests', async ({ baseUrl, expectedPrefix }) => {
    vi.stubEnv('VITE_API_BASE_URL', baseUrl)
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('{"status":"ok"}', {
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)
    const { apiRequest } = await import('../apiClient')

    await expect(apiRequest('/api/health', { authenticated: false })).resolves.toEqual({
      status: 'ok',
    })

    expect(fetchMock).toHaveBeenCalledWith(`${expectedPrefix}/api/health`, expect.any(Object))
  })

  it('uses the same origin for production PDF requests', async () => {
    vi.stubEnv('VITE_API_BASE_URL', '')
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValue(new Response(new Blob(['pdf'], { type: 'application/pdf' })))
    vi.stubGlobal('fetch', fetchMock)
    const { apiBlobRequest } = await import('../apiClient')

    await apiBlobRequest('/api/email-records/1/attachments/2/download')

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/email-records/1/attachments/2/download',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/pdf' }) }),
    )
  })
})
