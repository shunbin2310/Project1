import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import type { Product, CreateProductRequest, UpdateProductRequest } from '@/types/product'
import type { ProductCategory } from '@/types/productCategory'
import type { UnitOfMeasure } from '@/types/unitOfMeasure'
import ProductListView from '../ProductListView.vue'

const mocks = vi.hoisted(() => ({
  getProducts: vi.fn<() => Promise<Product[]>>(),
  create: vi.fn<(payload: CreateProductRequest) => Promise<Product>>(),
  update: vi.fn<(id: number, payload: UpdateProductRequest) => Promise<Product>>(),
  getCategories: vi.fn<() => Promise<ProductCategory[]>>(),
  getUnits: vi.fn<() => Promise<UnitOfMeasure[]>>(),
}))
vi.mock('@/services/productService', () => ({
  productService: { getAll: mocks.getProducts, create: mocks.create, update: mocks.update },
}))
vi.mock('@/services/productCategoryService', () => ({
  productCategoryService: { getAll: mocks.getCategories },
}))
vi.mock('@/services/unitOfMeasureService', () => ({
  unitOfMeasureService: { getAll: mocks.getUnits },
}))

const product: Product = {
  id: 1,
  code: 'ITEM-0001',
  name: 'Practice product',
  description: null,
  cicdPracticeNote: 'Deployment rehearsal',
  productCategoryId: 1,
  productCategoryCode: 'CAT-0001',
  productCategoryName: 'Practice category',
  unitOfMeasureId: 1,
  unitOfMeasureCode: 'UNIT',
  unitOfMeasureName: 'Unit',
  defaultUnitPrice: 12.5,
  reorderLevel: 3,
  isActive: true,
  createdAtUtc: '2026-10-10T00:00:00Z',
  updatedAtUtc: null,
}

describe('ProductListView practice note', () => {
  const wrappers: ReturnType<typeof mount>[] = []
  beforeEach(() => {
    vi.resetAllMocks()
    mocks.getProducts.mockResolvedValue([product])
    mocks.create.mockResolvedValue(product)
    mocks.update.mockResolvedValue(product)
    mocks.getCategories.mockResolvedValue([
      {
        id: 1,
        code: 'CAT-0001',
        name: 'Practice category',
        description: null,
        isActive: true,
        createdAtUtc: product.createdAtUtc,
        updatedAtUtc: null,
      },
    ])
    mocks.getUnits.mockResolvedValue([
      {
        id: 1,
        code: 'UNIT',
        name: 'Unit',
        description: null,
        isActive: true,
        createdAtUtc: product.createdAtUtc,
        updatedAtUtc: null,
      },
    ])
  })
  afterEach(() => {
    wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  })
  async function render() {
    const wrapper = mount(ProductListView)
    wrappers.push(wrapper)
    await flushPromises()
    return wrapper
  }

  it('displays and searches the practice note', async () => {
    const wrapper = await render()
    expect(wrapper.get('tbody').text()).toContain('Deployment rehearsal')
    await wrapper.get('input[type="search"]').setValue('REHEARSAL')
    expect(wrapper.get('tbody').text()).toContain('ITEM-0001')
    await wrapper.get('input[type="search"]').setValue('not a matching note')
    expect(wrapper.text()).toContain('No matching products')
  })

  it('sends the practice note when creating through the real form', async () => {
    const wrapper = await render()
    await wrapper.get('.page-heading button').trigger('click')
    await wrapper.get('#product-name').setValue('New practice product')
    await wrapper.get('#product-category').setValue('1')
    await wrapper.get('#product-unit').setValue('1')
    await wrapper.get('#product-cicd-practice-note').setValue('  New deployment note  ')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(mocks.create).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'New practice product',
        cicdPracticeNote: 'New deployment note',
      }),
    )
  })

  it('loads an existing note and sends null when it is cleared', async () => {
    const wrapper = await render()
    const edit = wrapper.findAll('tbody button').find((button) => button.text() === 'Edit')
    expect(edit).toBeDefined()
    await edit!.trigger('click')
    expect((wrapper.get('#product-cicd-practice-note').element as HTMLInputElement).value).toBe(
      product.cicdPracticeNote,
    )
    await wrapper.get('#product-cicd-practice-note').setValue('')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(mocks.update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ cicdPracticeNote: null }),
    )
  })

  it('preserves the practice note when reactivating a product', async () => {
    mocks.getProducts.mockResolvedValue([{ ...product, isActive: false }])
    const wrapper = await render()
    await wrapper.get('input[type="checkbox"]').setValue(true)
    const reactivate = wrapper
      .findAll('tbody button')
      .find((button) => button.text() === 'Reactivate')
    expect(reactivate).toBeDefined()
    await reactivate!.trigger('click')
    await flushPromises()
    expect(mocks.update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({
        isActive: true,
        cicdPracticeNote: 'Deployment rehearsal',
      }),
    )
  })
})
