import { describe, expect, it } from 'vitest'
import { createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'

import { useAuthStore } from '@/stores/auth'
import type { ApplicationRole } from '@/types/auth'
import AccessDeniedView from '../AccessDeniedView.vue'

describe('AccessDeniedView', () => {
  it.each<[ApplicationRole[], string]>([
    [['ADMIN'], '/departments'],
    [['REQUESTER'], '/my-tasks'],
    [['PROCUREMENT_OFFICER'], '/my-tasks'],
    [['WAREHOUSE_OFFICER'], '/dashboard'],
    [['WAREHOUSE_OFFICER', 'REQUESTER'], '/my-tasks'],
  ])('returns roles %j to an accessible workspace at %s', async (roles, path) => {
    const pinia = createPinia()
    useAuthStore(pinia).$patch({
      session: {
        accessToken: 'test-token',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        user: {
          id: 1,
          email: 'user@demo.local',
          fullName: 'Demo User',
          departmentId: null,
          departmentCode: null,
          departmentName: null,
          roles,
        },
      },
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/access-denied', component: AccessDeniedView }],
    })
    await router.push('/access-denied')
    const wrapper = mount(AccessDeniedView, { global: { plugins: [pinia, router] } })

    expect(wrapper.get('a').attributes('href')).toBe(path)
    wrapper.unmount()
  })
})
