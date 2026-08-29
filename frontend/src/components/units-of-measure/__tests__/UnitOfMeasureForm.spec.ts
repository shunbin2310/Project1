import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import UnitOfMeasureForm from '../UnitOfMeasureForm.vue'

describe('UnitOfMeasureForm', () => {
  it('allows the unit code to be entered when creating', () => {
    const wrapper = mount(UnitOfMeasureForm, {
      props: { unit: null, saving: false, errorMessage: '' },
    })

    expect(wrapper.get('#unit-code').attributes('readonly')).toBeUndefined()
  })

  it('shows the unit code as read-only when editing', () => {
    const wrapper = mount(UnitOfMeasureForm, {
      props: {
        unit: {
          id: 1,
          code: 'PCS',
          name: 'Pieces',
          description: null,
          isActive: true,
          createdAtUtc: '2026-08-29T00:00:00Z',
          updatedAtUtc: null,
        },
        saving: false,
        errorMessage: '',
      },
    })

    const codeInput = wrapper.get('#unit-code')
    expect(codeInput.attributes('readonly')).toBeDefined()
    expect((codeInput.element as HTMLInputElement).value).toBe('PCS')
  })

  it('shows an API error inside the form', () => {
    const wrapper = mount(UnitOfMeasureForm, {
      props: {
        unit: null,
        saving: false,
        errorMessage: "A unit of measure with code 'PCS' already exists.",
      },
    })

    expect(wrapper.get('[role="alert"]').text()).toContain('Unit of measure could not be saved')
    expect(wrapper.get('[role="alert"]').text()).toContain('already exists')
  })
})
