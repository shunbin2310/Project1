import { afterEach, describe, expect, it, vi } from 'vitest'

import { productService } from '../productService'

const productResponse = {
  id: 1,
  code: 'ITEM-0001',
  name: 'A4 Paper',
  description: null,
  productCategoryId: 1,
  productCategoryCode: 'CAT-0001',
  productCategoryName: 'Office Supplies',
  unitOfMeasureId: 1,
  unitOfMeasureCode: 'REAM',
  unitOfMeasureName: 'Ream',
  defaultUnitPrice: 18.9,
  reorderLevel: 10,
  isActive: true,
  createdAtUtc: '2026-08-29T00:00:00Z',
  updatedAtUtc: null,
}

describe('productService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests inactive products when requested', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    expect(await productService.getAll(true)).toEqual([])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/products?includeInactive=true',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('does not send a product code when creating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify(productResponse), {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await productService.create({
      name: 'A4 Paper',
      description: null,
      productCategoryId: 1,
      unitOfMeasureId: 1,
      defaultUnitPrice: 18.9,
      reorderLevel: 10,
    })

    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).not.toHaveProperty('code')
    expect(body).toMatchObject({ productCategoryId: 1, unitOfMeasureId: 1 })
  })

  it('does not send a product code when updating', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify(productResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await productService.update(1, {
      name: 'A4 Paper',
      description: null,
      productCategoryId: 1,
      unitOfMeasureId: 1,
      defaultUnitPrice: 18.9,
      reorderLevel: 10,
      isActive: true,
    })

    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, unknown>
    expect(body).not.toHaveProperty('code')
    expect(body.isActive).toBe(true)
  })

  it('uses related-record error details returned by the API', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify({ detail: 'Select an active product category.' }), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const request = productService.create({
      name: 'A4 Paper',
      description: null,
      productCategoryId: 1,
      unitOfMeasureId: 1,
      defaultUnitPrice: 18.9,
      reorderLevel: 10,
    })

    await expect(request).rejects.toMatchObject({
      status: 400,
      message: 'Select an active product category.',
    })
  })
})
