import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import ProductForm from '../ProductForm.vue'

describe('ProductForm', () => {
  it('does not show a product code when creating', () => {
    const wrapper = mount(ProductForm, {
      props: {
        product: null,
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.find('#product-code').exists()).toBe(false)
  })

  it('shows the generated product code as read-only when editing', () => {
    const wrapper = mount(ProductForm, {
      props: {
        product: {
          id: 1,
          code: 'ITEM-0001',
          name: 'A4 Paper',
          description: null,
          storageLocation: 'Shelf A1',
          defaultUnitPrice: 18.9,
          reorderLevel: 10,
          isActive: true,
          createdAtUtc: '2026-08-29T00:00:00Z',
          updatedAtUtc: null,
        },
        saving: false,
        errorMessage: '',
      },
    })

    const codeInput = wrapper.get('#product-code')
    expect(codeInput.attributes('readonly')).toBeDefined()
    expect((codeInput.element as HTMLInputElement).value).toBe('ITEM-0001')
    expect(wrapper.find('#product-cicd-practice-note').exists()).toBe(false)
    expect((wrapper.get('#product-storage-location').element as HTMLInputElement).value).toBe(
      'Shelf A1',
    )
    expect(wrapper.get('#product-storage-location').attributes('maxlength')).toBe('100')
  })

  it.each([
    ['  Shelf A1  ', 'Shelf A1'],
    ['', null],
    ['   ', null],
    ['x'.repeat(100), 'x'.repeat(100)],
  ])('saves normalized optional storage location %j', async (input, expected) => {
    const wrapper = mount(ProductForm, {
      props: { product: null, saving: false, errorMessage: '' },
    })
    await wrapper.get('#product-name').setValue('Storage product')
    await wrapper.get('#product-storage-location').setValue(input)
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({ storageLocation: expected })
  })

  it('rejects a storage location longer than 100 characters', async () => {
    const wrapper = mount(ProductForm, {
      props: { product: null, saving: false, errorMessage: '' },
    })
    await wrapper.get('#product-name').setValue('Storage product')
    await wrapper.get('#product-storage-location').setValue('x'.repeat(101))
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('save')).toBeUndefined()
    expect(wrapper.get('#product-storage-location-error').text()).toContain('100 characters')
    expect(wrapper.get('#product-storage-location').attributes('aria-invalid')).toBe('true')
  })

  it('does not ask for a category or unit when creating', () => {
    const wrapper = mount(ProductForm, {
      props: { product: null, saving: false, errorMessage: '' },
    })
    expect(wrapper.find('#product-category').exists()).toBe(false)
    expect(wrapper.find('#product-unit').exists()).toBe(false)
  })

  it('shows an API error inside the form', () => {
    const wrapper = mount(ProductForm, {
      props: {
        product: null,
        saving: false,
        errorMessage: 'Reorder level is invalid.',
      },
    })

    expect(wrapper.get('[role="alert"]').text()).toContain('Product could not be saved')
    expect(wrapper.get('[role="alert"]').text()).toContain('Reorder level is invalid.')
  })

  it('rejects fractional reorder levels and accepts whole numbers with decimal prices', async () => {
    const wrapper = mount(ProductForm, {
      props: { product: null, saving: false, errorMessage: '' },
    })
    await wrapper.get('#product-name').setValue('Monitor')
    await wrapper.get('#product-price').setValue('12.50')
    await wrapper.get('#product-reorder-level').setValue('1.004')
    expect(wrapper.get('#product-reorder-level').attributes('step')).toBe('1')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('save')).toBeUndefined()
    expect(wrapper.text()).toContain('whole number')
    await wrapper.get('#product-reorder-level').setValue('4')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({
      reorderLevel: 4,
      defaultUnitPrice: 12.5,
    })
    expect(wrapper.emitted('save')?.[0]?.[0]).not.toHaveProperty('productCategoryId')
    expect(wrapper.emitted('save')?.[0]?.[0]).not.toHaveProperty('unitOfMeasureId')
    wrapper.unmount()
  })
})
