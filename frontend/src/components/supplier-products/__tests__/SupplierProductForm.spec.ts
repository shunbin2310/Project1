import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { Product } from '@/types/product'
import type { Supplier } from '@/types/supplier'
import type { SupplierProduct } from '@/types/supplierProduct'
import SupplierProductForm from '../SupplierProductForm.vue'

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
    name: 'Inactive Supplies',
    contactPerson: null,
    email: null,
    phone: null,
    address: null,
    isActive: false,
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

const relationship: SupplierProduct = {
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
  isPreferred: false,
  isActive: true,
  createdAtUtc: '2026-09-01T00:00:00Z',
  updatedAtUtc: null,
}

function mountForm(supplierProduct: SupplierProduct | null = null) {
  return mount(SupplierProductForm, {
    props: {
      supplierProduct,
      suppliers,
      products,
      saving: false,
      errorMessage: '',
    },
  })
}

describe('SupplierProductForm', () => {
  it('requires a supplier and product before creation', async () => {
    const wrapper = mountForm()

    await wrapper.get('form').trigger('submit')

    expect(wrapper.text()).toContain('Select a supplier.')
    expect(wrapper.text()).toContain('Select a product.')
    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('lists only active suppliers when creating', () => {
    const wrapper = mountForm()

    expect(wrapper.get('#relationship-supplier').text()).toContain('Example Supplies')
    expect(wrapper.get('#relationship-supplier').text()).not.toContain('Inactive Supplies')
  })

  it('shows the selected product default price and emits creation values', async () => {
    const wrapper = mountForm()

    await wrapper.get('#relationship-supplier').setValue('1')
    await wrapper.get('#relationship-product').setValue('1')
    await wrapper.findAll('input[type="checkbox"]')[0]!.setValue(true)
    await wrapper.get('form').trigger('submit')

    expect(wrapper.text().replace(/\s/g, '')).toContain('RM1,399.90/UNIT')
    expect(wrapper.emitted('save')?.[0]).toEqual([
      {
        supplierId: 1,
        productId: 1,
        isPreferred: true,
        isActive: true,
      },
    ])
  })

  it('keeps supplier and product read-only when editing', () => {
    const wrapper = mountForm(relationship)

    expect(wrapper.find('#relationship-supplier').exists()).toBe(false)
    expect(wrapper.find('#relationship-product').exists()).toBe(false)
    expect(wrapper.get('#relationship-supplier-readonly').attributes('readonly')).toBeDefined()
    expect(wrapper.get('#relationship-product-readonly').attributes('readonly')).toBeDefined()
  })

  it('shows an API error inside the form', () => {
    const wrapper = mount(SupplierProductForm, {
      props: {
        supplierProduct: null,
        suppliers,
        products,
        saving: false,
        errorMessage: 'The selected supplier is already linked to this product.',
      },
    })

    expect(wrapper.get('[role="alert"]').text()).toContain('Relationship could not be saved')
    expect(wrapper.get('[role="alert"]').text()).toContain('already linked')
  })
})
