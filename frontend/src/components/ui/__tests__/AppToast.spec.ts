import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import AppToast from '../AppToast.vue'

describe('AppToast', () => {
  it('renders a success notification and emits dismiss', async () => {
    const wrapper = mount(AppToast, {
      props: {
        toast: {
          title: 'Action completed',
          message: 'SUP-0001 was created successfully.',
          variant: 'success',
        },
      },
    })

    expect(wrapper.get('.success-toast').attributes('role')).toBe('status')
    expect(wrapper.text()).toContain('SUP-0001 was created successfully.')

    await wrapper.get('[aria-label="Dismiss notification"]').trigger('click')
    expect(wrapper.emitted('dismiss')).toHaveLength(1)
  })

  it('uses an assertive alert for an error notification', () => {
    const wrapper = mount(AppToast, {
      props: {
        toast: {
          title: 'Action failed',
          message: 'The record could not be updated.',
          variant: 'error',
        },
      },
    })

    expect(wrapper.get('.error-toast').attributes('role')).toBe('alert')
    expect(wrapper.get('.error-toast').attributes('aria-live')).toBe('assertive')
  })
})
