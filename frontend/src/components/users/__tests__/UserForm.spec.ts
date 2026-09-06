import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import UserForm from '../UserForm.vue'

const roles = ['REQUESTER', 'DEPARTMENT_APPROVER', 'FINANCE_APPROVER', 'ADMIN'] as const

describe('UserForm', () => {
  it('locks email and the current administrators Admin role when editing self', () => {
    const wrapper = mount(UserForm, {
      props: {
        user: {
          id: 4,
          email: 'admin@demo.local',
          fullName: 'Demo Admin',
          departmentId: 1,
          departmentCode: 'IT',
          departmentName: 'Information Technology',
          isActive: true,
          roles: ['ADMIN'],
          createdAtUtc: '2026-08-31T00:00:00Z',
        },
        currentUserId: 4,
        availableRoles: [...roles],
        departments: [],
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.get('#user-email').attributes('readonly')).toBeDefined()
    expect(wrapper.get('input[value="ADMIN"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('You cannot remove your own Admin role.')
  })

  it('emits a new user without asking for a password', async () => {
    const wrapper = mount(UserForm, {
      props: {
        user: null,
        currentUserId: 4,
        availableRoles: [...roles],
        departments: [
          {
            id: 1,
            code: 'IT',
            name: 'Information Technology',
            description: null,
            isActive: true,
            createdAtUtc: '2026-08-31T00:00:00Z',
            updatedAtUtc: null,
          },
        ],
        saving: false,
        errorMessage: '',
      },
    })

    await wrapper.get('#user-email').setValue('new.user@demo.local')
    await wrapper.get('#user-full-name').setValue('New User')
    await wrapper.get('#user-department').setValue('1')
    await wrapper.get('input[value="REQUESTER"]').setValue(true)
    await wrapper.get('input[value="DEPARTMENT_APPROVER"]').setValue(true)
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({
      email: 'new.user@demo.local',
      fullName: 'New User',
      departmentId: 1,
      roles: ['REQUESTER', 'DEPARTMENT_APPROVER'],
    })
    expect(wrapper.find('#user-temporary-password').exists()).toBe(false)
  })

  it('keeps only Admin when Admin is selected', async () => {
    const wrapper = mount(UserForm, {
      props: {
        user: null,
        currentUserId: 4,
        availableRoles: [...roles],
        departments: [],
        saving: false,
        errorMessage: '',
      },
    })

    await wrapper.get('#user-email').setValue('admin.two@demo.local')
    await wrapper.get('#user-full-name').setValue('Second Admin')
    await wrapper.get('input[value="REQUESTER"]').setValue(true)
    await wrapper.get('input[value="ADMIN"]').setValue(true)
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({ roles: ['ADMIN'] })
    expect(wrapper.get('input[value="REQUESTER"]').attributes('disabled')).toBeDefined()
  })
})
