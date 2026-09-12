import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import SupplierProductForm from '@/components/supplier-products/SupplierProductForm.vue'
import type { Product } from '@/types/product'
import type { Supplier } from '@/types/supplier'
import type {
  CreateSupplierProductRequest,
  SupplierProduct,
  SupplierProductFilters,
  UpdateSupplierProductRequest,
} from '@/types/supplierProduct'
import SupplierProductListView from '../SupplierProductListView.vue'

const mocks = vi.hoisted(() => ({
  getRelationships: vi.fn<(filters?: SupplierProductFilters) => Promise<SupplierProduct[]>>(),
  createRelationship: vi.fn<(payload: CreateSupplierProductRequest) => Promise<SupplierProduct>>(),
  updateRelationship:
    vi.fn<(id: number, payload: UpdateSupplierProductRequest) => Promise<SupplierProduct>>(),
  deleteRelationship: vi.fn<(id: number) => Promise<void>>(),
  getSuppliers: vi.fn<(includeInactive?: boolean) => Promise<Supplier[]>>(),
  getProducts: vi.fn<(includeInactive?: boolean) => Promise<Product[]>>(),
}))

vi.mock('@/services/supplierProductService', () => ({
  supplierProductService: {
    getAll: mocks.getRelationships,
    create: mocks.createRelationship,
    update: mocks.updateRelationship,
    delete: mocks.deleteRelationship,
  },
}))

vi.mock('@/services/supplierService', () => ({
  supplierService: { getAll: mocks.getSuppliers },
}))

vi.mock('@/services/productService', () => ({
  productService: { getAll: mocks.getProducts },
}))

const suppliers: Supplier[] = [
  {
    id: 1,
    code: 'SUP-0001',
    name: 'Example Supplies',
    contactPerson: null,
    email: null,
    phone: null,
    address: null,
    isActive: true,
    createdAtUtc: '2026-09-01T00:00:00Z',
    updatedAtUtc: null,
  },
  {
    id: 2,
    code: 'SUP-0002',
    name: 'ABC Trading',
    contactPerson: null,
    email: null,
    phone: null,
    address: null,
    isActive: true,
    createdAtUtc: '2026-09-01T00:00:00Z',
    updatedAtUtc: null,
  },
]

const products: Product[] = [
  {
    id: 1,
    code: 'ITEM-0001',
    name: 'Dell Monitor',
    description: null,
    productCategoryId: 1,
    productCategoryCode: 'CAT-0001',
    productCategoryName: 'Electronics',
    unitOfMeasureId: 1,
    unitOfMeasureCode: 'UNIT',
    unitOfMeasureName: 'Unit',
    defaultUnitPrice: 1399.9,
    reorderLevel: 5,
    isActive: true,
    createdAtUtc: '2026-09-01T00:00:00Z',
    updatedAtUtc: null,
  },
]

const relationships: SupplierProduct[] = [
  {
    id: 1,
    supplierId: 1,
    supplierCode: 'SUP-0001',
    supplierName: 'Example Supplies',
    productId: 1,
    productCode: 'ITEM-0001',
    productName: 'Dell Monitor',
    unitOfMeasureCode: 'UNIT',
    unitOfMeasureName: 'Unit',
    productDefaultUnitPrice: 1399.9,
    isPreferred: true,
    isActive: true,
    createdAtUtc: '2026-09-01T00:00:00Z',
    updatedAtUtc: null,
  },
  {
    id: 2,
    supplierId: 2,
    supplierCode: 'SUP-0002',
    supplierName: 'ABC Trading',
    productId: 1,
    productCode: 'ITEM-0001',
    productName: 'Dell Monitor',
    unitOfMeasureCode: 'UNIT',
    unitOfMeasureName: 'Unit',
    productDefaultUnitPrice: 1399.9,
    isPreferred: false,
    isActive: false,
    createdAtUtc: '2026-09-01T00:00:00Z',
    updatedAtUtc: null,
  },
]

describe('SupplierProductListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getRelationships.mockResolvedValue(relationships)
    mocks.getSuppliers.mockResolvedValue(suppliers)
    mocks.getProducts.mockResolvedValue(products)
    mocks.createRelationship.mockResolvedValue(relationships[0]!)
    mocks.updateRelationship.mockResolvedValue(relationships[0]!)
    mocks.deleteRelationship.mockResolvedValue(undefined)
    vi.stubGlobal(
      'confirm',
      vi.fn(() => true),
    )
  })

  it('loads relationships and supports supplier filtering', async () => {
    const wrapper = mount(SupplierProductListView)
    await flushPromises()

    expect(mocks.getRelationships).toHaveBeenCalledWith({ includeInactive: true })
    expect(wrapper.get('h1').text()).toBe('Supplier Products')
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)
    expect(wrapper.text().replace(/\s/g, '')).toContain('RM1,399.90')

    await wrapper.get('[aria-label="Filter by supplier"]').setValue('1')

    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    expect(wrapper.get('tbody').text()).toContain('Example Supplies')
    expect(wrapper.get('tbody').text()).not.toContain('ABC Trading')
  })

  it('creates a relationship, closes the form, and refreshes the list', async () => {
    const wrapper = mount(SupplierProductListView)
    await flushPromises()

    await wrapper.get('button.button-primary').trigger('click')
    wrapper.findComponent(SupplierProductForm).vm.$emit('save', {
      supplierId: 1,
      productId: 1,
      isPreferred: true,
      isActive: true,
    })
    await flushPromises()

    expect(mocks.createRelationship).toHaveBeenCalledWith({
      supplierId: 1,
      productId: 1,
      isPreferred: true,
    })
    expect(wrapper.findComponent(SupplierProductForm).exists()).toBe(false)
    expect(mocks.getRelationships).toHaveBeenCalledTimes(2)
    expect(wrapper.get('.success-toast').text()).toContain('SUP-0001 is now linked to ITEM-0001')
  })

  it('shows a duplicate relationship error inside the open form', async () => {
    mocks.createRelationship.mockRejectedValueOnce(
      new Error('The selected supplier is already linked to this product.'),
    )
    const wrapper = mount(SupplierProductListView)
    await flushPromises()

    await wrapper.get('button.button-primary').trigger('click')
    wrapper.findComponent(SupplierProductForm).vm.$emit('save', {
      supplierId: 1,
      productId: 1,
      isPreferred: false,
      isActive: true,
    })
    await flushPromises()

    expect(wrapper.findComponent(SupplierProductForm).exists()).toBe(true)
    expect(wrapper.get('[role="dialog"] [role="alert"]').text()).toContain('already linked')
  })

  it('updates only the relationship settings', async () => {
    const wrapper = mount(SupplierProductListView)
    await flushPromises()

    const editButton = wrapper.findAll('button').find((button) => button.text() === 'Edit')
    await editButton?.trigger('click')
    wrapper.findComponent(SupplierProductForm).vm.$emit('save', {
      supplierId: 1,
      productId: 1,
      isPreferred: false,
      isActive: false,
    })
    await flushPromises()

    expect(mocks.updateRelationship).toHaveBeenCalledWith(1, {
      isPreferred: false,
      isActive: false,
    })
  })

  it('deletes only the selected relationship after confirmation', async () => {
    const wrapper = mount(SupplierProductListView)
    await flushPromises()

    const deleteButton = wrapper.findAll('button').find((button) => button.text() === 'Delete')
    await deleteButton?.trigger('click')
    await flushPromises()

    expect(window.confirm).toHaveBeenCalled()
    expect(mocks.deleteRelationship).toHaveBeenCalledWith(1)
    expect(mocks.getRelationships).toHaveBeenCalledTimes(2)
    expect(wrapper.get('.success-toast').text()).toContain('are no longer linked')
  })
})
