import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import ProductForm from '../ProductForm.vue'

const category = {
  id: 1,
  code: 'CAT-0001',
  name: 'Office Supplies',
  description: null,
  isActive: true,
  createdAtUtc: '2026-08-29T00:00:00Z',
  updatedAtUtc: null,
}

const unit = {
  id: 1,
  code: 'REAM',
  name: 'Ream',
  description: null,
  isActive: true,
  createdAtUtc: '2026-08-29T00:00:00Z',
  updatedAtUtc: null,
}

describe('ProductForm', () => {
  it('does not show a product code when creating', () => {
    const wrapper = mount(ProductForm, {
      props: {
        product: null,
        categories: [category],
        units: [unit],
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
          cicdPracticeNote: 'First deployment exercise',
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
        },
        categories: [category],
        units: [unit],
        saving: false,
        errorMessage: '',
      },
    })

    const codeInput = wrapper.get('#product-code')
    expect(codeInput.attributes('readonly')).toBeDefined()
    expect((codeInput.element as HTMLInputElement).value).toBe('ITEM-0001')
    expect((wrapper.get('#product-cicd-practice-note').element as HTMLInputElement).value).toBe(
      'First deployment exercise',
    )
    expect(wrapper.get('#product-cicd-practice-note').attributes('maxlength')).toBe('100')
  })

  it.each([
    ['  Deployment practice  ', 'Deployment practice'],
    ['', null],
    ['   ', null],
    ['x'.repeat(100), 'x'.repeat(100)],
  ])('saves normalized optional practice note %j', async (input, expected) => {
    const wrapper = mount(ProductForm, {
      props: {
        product: null,
        categories: [category],
        units: [unit],
        saving: false,
        errorMessage: '',
      },
    })
    await wrapper.get('#product-name').setValue('Practice product')
    await wrapper.get('#product-category').setValue('1')
    await wrapper.get('#product-unit').setValue('1')
    await wrapper.get('#product-cicd-practice-note').setValue(input)
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({ cicdPracticeNote: expected })
  })

  it('rejects a practice note longer than 100 characters', async () => {
    const wrapper = mount(ProductForm, {
      props: {
        product: null,
        categories: [category],
        units: [unit],
        saving: false,
        errorMessage: '',
      },
    })
    await wrapper.get('#product-name').setValue('Practice product')
    await wrapper.get('#product-category').setValue('1')
    await wrapper.get('#product-unit').setValue('1')
    await wrapper.get('#product-cicd-practice-note').setValue('x'.repeat(101))
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('save')).toBeUndefined()
    expect(wrapper.get('#product-cicd-practice-note-error').text()).toContain('100 characters')
    expect(wrapper.get('#product-cicd-practice-note').attributes('aria-invalid')).toBe('true')
  })

  it('only offers active related records when creating', () => {
    const wrapper = mount(ProductForm, {
      props: {
        product: null,
        categories: [{ ...category, id: 2, isActive: false }],
        units: [{ ...unit, id: 2, isActive: false }],
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.findAll('#product-category option')).toHaveLength(1)
    expect(wrapper.findAll('#product-unit option')).toHaveLength(1)
  })

  it('shows an API error inside the form', () => {
    const wrapper = mount(ProductForm, {
      props: {
        product: null,
        categories: [category],
        units: [unit],
        saving: false,
        errorMessage: 'Select an active product category.',
      },
    })

    expect(wrapper.get('[role="alert"]').text()).toContain('Product could not be saved')
    expect(wrapper.get('[role="alert"]').text()).toContain('Select an active product category.')
  })
})
