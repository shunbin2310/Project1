import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import ProductCategoryForm from '../ProductCategoryForm.vue'

describe('ProductCategoryForm', () => {
  it('does not show a category code when creating', () => {
    const wrapper = mount(ProductCategoryForm, {
      props: { category: null, saving: false, errorMessage: '' },
    })

    expect(wrapper.find('#product-category-code').exists()).toBe(false)
  })

  it('shows the category code as read-only when editing', () => {
    const wrapper = mount(ProductCategoryForm, {
      props: {
        category: {
          id: 1,
          code: 'CAT-0001',
          name: 'Office Supplies',
          description: null,
          isActive: true,
          createdAtUtc: '2026-08-29T00:00:00Z',
          updatedAtUtc: null,
        },
        saving: false,
        errorMessage: '',
      },
    })

    const codeInput = wrapper.get('#product-category-code')
    expect(codeInput.attributes('readonly')).toBeDefined()
    expect((codeInput.element as HTMLInputElement).value).toBe('CAT-0001')
  })

  it('shows an API error inside the form', () => {
    const wrapper = mount(ProductCategoryForm, {
      props: {
        category: null,
        saving: false,
        errorMessage: "A product category with name 'Office Supplies' already exists.",
      },
    })

    expect(wrapper.get('[role="alert"]').text()).toContain('Product category could not be saved')
    expect(wrapper.get('[role="alert"]').text()).toContain('already exists')
  })
})
