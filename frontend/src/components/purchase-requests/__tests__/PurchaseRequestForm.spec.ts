import { describe, expect, it } from 'vitest'
import { createPinia } from 'pinia'

import { mount } from '@vue/test-utils'
import { useAuthStore } from '@/stores/auth'
import PurchaseRequestForm from '../PurchaseRequestForm.vue'

const product = {
  id: 1,
  code: 'ITEM-0001',
  name: 'Monitor',
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
  createdAtUtc: '2026-08-29T00:00:00Z',
  updatedAtUtc: null,
}

function mountForm() {
  const pinia = createPinia()
  const authStore = useAuthStore(pinia)
  authStore.$patch({
    session: {
      accessToken: 'test-token',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      user: {
        id: 1,
        email: 'requester@demo.local',
        fullName: 'Demo Requester',
        departmentId: 1,
        departmentCode: 'IT',
        departmentName: 'Information Technology',
        roles: ['REQUESTER'],
      },
    },
  })

  return mount(PurchaseRequestForm, {
    props: {
      purchaseRequest: null,
      products: [product],
      saving: false,
      errorMessage: '',
    },
    global: { plugins: [pinia] },
  })
}

describe('PurchaseRequestForm', () => {
  it('creates an empty draft form without forcing an item', async () => {
    const wrapper = mountForm()

    expect(wrapper.text()).toContain('No items added to this draft.')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({ items: [] })
    expect(wrapper.emitted('save')?.[0]?.[1]).toBe(false)
  })

  it('validates and emits a complete create-and-submit request', async () => {
    const wrapper = mountForm()

    await wrapper.get('#purchase-required-date').setValue('2030-12-31')
    const addItemButton = wrapper.findAll('button').find((button) => button.text() === '+ Add item')
    await addItemButton?.trigger('click')
    await wrapper.get('#purchase-product-0').setValue('1')
    await wrapper.get('#purchase-product-0').trigger('change')
    const submitButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Create and submit')
    await submitButton?.trigger('click')

    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({
      requiredDate: '2030-12-31',
      items: [{ productId: 1, quantity: 1, estimatedUnitPrice: 1399.9 }],
    })
    expect(wrapper.emitted('save')?.[0]?.[1]).toBe(true)
  })

  it('does not fast-submit an incomplete draft', async () => {
    const wrapper = mountForm()
    const submitButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Create and submit')

    await submitButton?.trigger('click')

    expect(wrapper.emitted('save')).toBeUndefined()
    expect(wrapper.text()).toContain('Required date is required before submission.')
    expect(wrapper.text()).toContain('Add at least one item before submission.')
  })

  it('adds an item and applies the product default price', async () => {
    const wrapper = mountForm()

    await wrapper.get('button.button-secondary').trigger('click')
    await wrapper.get('#purchase-product-0').setValue('1')
    await wrapper.get('#purchase-product-0').trigger('change')

    expect((wrapper.get('#purchase-price-0').element as HTMLInputElement).value).toBe('1399.9')
  })

  it('shows an API workflow validation error inside the form', async () => {
    const wrapper = mountForm()
    await wrapper.setProps({ errorMessage: 'No active published workflow template exists.' })

    expect(wrapper.get('[role="alert"]').text()).toContain('Purchase request could not be saved')
    expect(wrapper.get('[role="alert"]').text()).toContain(
      'No active published workflow template exists.',
    )
  })
})
