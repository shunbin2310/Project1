import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import SupplierForm from '../SupplierForm.vue'

describe('SupplierForm', () => {
  it('shows a save error inside the form', () => {
    const wrapper = mount(SupplierForm, {
      props: {
        supplier: null,
        saving: false,
        errorMessage: 'A supplier with code SUP-001 already exists.',
      },
    })

    const alert = wrapper.get('[role="alert"]')

    expect(alert.text()).toContain('Supplier could not be saved')
    expect(alert.text()).toContain('A supplier with code SUP-001 already exists.')
  })

  it('does not show a supplier code field when creating', () => {
    const wrapper = mount(SupplierForm, {
      props: {
        supplier: null,
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.find('#supplier-code').exists()).toBe(false)
  })

  it('makes the supplier code read-only when editing', () => {
    const wrapper = mount(SupplierForm, {
      props: {
        supplier: {
          id: 1,
          code: 'SUP-001',
          name: 'Example Supplies',
          contactPerson: null,
          email: null,
          phone: null,
          address: null,
          isActive: true,
          createdAtUtc: '2026-08-25T00:00:00Z',
          updatedAtUtc: null,
        },
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.get('#supplier-code').attributes('readonly')).toBeDefined()
    expect(wrapper.text()).toContain('Supplier code cannot be changed after creation.')
  })

  it('does not submit an invalid email address', async () => {
    const wrapper = mount(SupplierForm, {
      props: {
        supplier: null,
        saving: false,
        errorMessage: '',
      },
    })

    await wrapper.get('#supplier-name').setValue('Example Supplies')
    await wrapper.get('#supplier-email').setValue('not-an-email')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.text()).toContain('Enter a valid email address.')
    expect(wrapper.emitted('save')).toBeUndefined()
  })
})
