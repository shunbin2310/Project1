import { describe, expect, it } from 'vitest'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'

import { mount } from '@vue/test-utils'
import { useAuthStore } from '@/stores/auth'
import App from '../App.vue'

describe('App', () => {
  it('renders the workspace navigation and current page', async () => {
    const pinia = createPinia()
    const authStore = useAuthStore(pinia)
    authStore.$patch({
      session: {
        accessToken: 'test-token',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        user: {
          id: 4,
          email: 'admin@demo.local',
          fullName: 'Demo Admin',
          departmentId: 1,
          departmentCode: 'IT',
          departmentName: 'Information Technology',
          roles: ['ADMIN', 'REQUESTER', 'DEPARTMENT_APPROVER', 'FINANCE_APPROVER'],
        },
      },
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: '/departments',
          component: { template: '<h1>Departments</h1>' },
          meta: { title: 'Departments' },
        },
      ],
    })

    await router.push('/departments')
    await router.isReady()

    const wrapper = mount(App, {
      global: { plugins: [pinia, router] },
    })

    expect(wrapper.text()).toContain('Purchase & Inventory')
    expect(wrapper.get('h1').text()).toBe('Departments')
    expect(wrapper.get('a.router-link-active').text()).toContain('Departments')
    expect(wrapper.get('a[href="/suppliers"]').text()).toContain('Suppliers')
    expect(wrapper.get('a[href="/product-categories"]').text()).toContain('Product Categories')
    expect(wrapper.get('a[href="/units-of-measure"]').text()).toContain('Units of Measure')
    expect(wrapper.get('a[href="/products"]').text()).toContain('Products')
    expect(wrapper.get('a[href="/my-tasks"]').text()).toContain('My Tasks')
    expect(wrapper.get('a[href="/purchase-requests"]').text()).toContain('Purchase Requests')
  })

  it('hides administration navigation from a requester', async () => {
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
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: '/purchase-requests',
          component: { template: '<h1>Purchase Requests</h1>' },
          meta: { title: 'Purchase Requests' },
        },
      ],
    })

    await router.push('/purchase-requests')
    await router.isReady()

    const wrapper = mount(App, { global: { plugins: [pinia, router] } })

    expect(wrapper.find('a[href="/departments"]').exists()).toBe(false)
    expect(wrapper.get('a[href="/my-tasks"]').text()).toContain('My Tasks')
    expect(wrapper.get('a[href="/purchase-requests"]').text()).toContain('Purchase Requests')
  })
})
