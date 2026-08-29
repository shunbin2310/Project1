import { afterEach, describe, expect, it, vi } from 'vitest'

import { productCategoryService } from '../productCategoryService'

describe('productCategoryService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests inactive categories when requested', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    expect(await productCategoryService.getAll(true)).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/product-categories?includeInactive=true',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('does not send a category code when creating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 1,
          code: 'CAT-0001',
          name: 'Office Supplies',
          description: null,
          isActive: true,
          createdAtUtc: '2026-08-29T00:00:00Z',
          updatedAtUtc: null,
        }),
        { status: 201, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await productCategoryService.create({ name: 'Office Supplies', description: null })

    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).toEqual({ name: 'Office Supplies', description: null })
    expect(body).not.toHaveProperty('code')
  })

  it('uses the conflict detail returned by the API', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          title: 'Product category name already exists.',
          detail: "A product category with name 'Office Supplies' already exists.",
        }),
        { status: 409, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const request = productCategoryService.create({
      name: 'Office Supplies',
      description: null,
    })

    await expect(request).rejects.toMatchObject({
      status: 409,
      message: "A product category with name 'Office Supplies' already exists.",
    })
  })
})
