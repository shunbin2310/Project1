import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import DepartmentForm from '../DepartmentForm.vue'

describe('DepartmentForm', () => {
  it('shows a save error inside the form', () => {
    const wrapper = mount(DepartmentForm, {
      props: {
        department: null,
        saving: false,
        errorMessage: 'A department with code IT already exists.',
      },
    })

    const alert = wrapper.get('[role="alert"]')

    expect(alert.text()).toContain('Department could not be saved')
    expect(alert.text()).toContain('A department with code IT already exists.')
  })

  it('makes the department code read-only when editing', () => {
    const wrapper = mount(DepartmentForm, {
      props: {
        department: {
          id: 1,
          code: 'IT',
          name: 'Information Technology',
          description: null,
          isActive: true,
          createdAtUtc: '2026-08-25T00:00:00Z',
          updatedAtUtc: null,
        },
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.get('#department-code').attributes('readonly')).toBeDefined()
    expect(wrapper.text()).toContain('Department code cannot be changed after creation.')
  })
})
