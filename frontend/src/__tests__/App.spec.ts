import { describe, expect, it } from 'vitest'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'

import { mount } from '@vue/test-utils'
import { useAuthStore } from '@/stores/auth'
import type { ApplicationRole } from '@/types/auth'
import LoginView from '@/views/auth/LoginView.vue'
import App from '../App.vue'

async function mountForRole(role: ApplicationRole) {
  const pinia = createPinia()
  const authStore = useAuthStore(pinia)
  authStore.$patch({
    session: {
      accessToken: 'test-token',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      user: {
        id: 10,
        email: 'user@demo.local',
        fullName: 'Demo User',
        departmentId: 1,
        departmentCode: 'TEST',
        departmentName: 'Test Department',
        roles: [role],
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

  return mount(App, { global: { plugins: [pinia, router] } })
}

describe('App', () => {
  it('renders the updated brand on the login page', async () => {
    const pinia = createPinia()
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: '/login',
          component: LoginView,
          meta: { title: 'Sign in', layout: 'auth' },
        },
      ],
    })
    await router.push('/login')
    await router.isReady()

    const wrapper = mount(App, { global: { plugins: [pinia, router] } })

    expect(wrapper.get('.auth-brand strong').text()).toBe('Project1 Inventory')
    wrapper.unmount()
  })

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
          roles: ['ADMIN'],
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

    expect(wrapper.get('.brand-block strong').text()).toBe('Project1 Inventory')
    expect(wrapper.get('h1').text()).toBe('Departments')
    expect(wrapper.get('a.router-link-active').text()).toContain('Departments')
    expect(wrapper.get('a[href="/suppliers"]').text()).toContain('Suppliers')
    expect(wrapper.get('a[href="/supplier-products"]').text()).toContain('Supplier Products')
    expect(wrapper.get('a[href="/users"]').text()).toContain('Users')
    expect(wrapper.get('a[href="/workflow-templates"]').text()).toContain('Workflow Templates')
    expect(wrapper.get('a[href="/product-categories"]').text()).toContain('Product Categories')
    expect(wrapper.get('a[href="/units-of-measure"]').text()).toContain('Units of Measure')
    expect(wrapper.get('a[href="/products"]').text()).toContain('Products')
    expect(wrapper.get('a[href="/my-tasks"]').text()).toContain('My Tasks')
    expect(wrapper.get('a[href="/dashboard"]').text()).toContain('Dashboard')
    expect(wrapper.get('a[href="/purchase-requests"]').text()).toContain('Purchase Requests')
    expect(wrapper.get('a[href="/quotations"]').text()).toContain('Supplier Quotations')
    expect(wrapper.get('a[href="/purchase-orders"]').text()).toContain('Purchase Orders')
    expect(wrapper.get('a[href="/email-records"]').text()).toContain('Email Records')
    expect(wrapper.get('a[href="/goods-receipts"]').text()).toContain('Goods Receiving')
    expect(wrapper.get('a[href="/inventory"]').text()).toContain('Inventory')
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
    expect(wrapper.find('a[href="/users"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/workflow-templates"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/supplier-products"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/quotations"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/purchase-orders"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/email-records"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/goods-receipts"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/inventory"]').exists()).toBe(false)
    expect(wrapper.get('a[href="/my-tasks"]').text()).toContain('My Tasks')
    expect(wrapper.get('a[href="/dashboard"]').text()).toContain('Dashboard')
    expect(wrapper.get('a[href="/purchase-requests"]').text()).toContain('Purchase Requests')
  })

  it('shows purchasing navigation to a procurement officer', async () => {
    const wrapper = await mountForRole('PROCUREMENT_OFFICER')

    expect(wrapper.get('a[href="/suppliers"]').text()).toContain('Suppliers')
    expect(wrapper.get('a[href="/supplier-products"]').text()).toContain('Supplier Products')
    expect(wrapper.get('a[href="/quotations"]').text()).toContain('Supplier Quotations')
    expect(wrapper.get('a[href="/purchase-orders"]').text()).toContain('Purchase Orders')
    expect(wrapper.get('a[href="/email-records"]').text()).toContain('Email Records')
    expect(wrapper.get('a[href="/goods-receipts"]').text()).toContain('Goods Receiving')
    expect(wrapper.get('a[href="/inventory"]').text()).toContain('Inventory')
    expect(wrapper.find('a[href="/users"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/products"]').exists()).toBe(false)
  })

  it('shows receiving navigation to a warehouse officer', async () => {
    const wrapper = await mountForRole('WAREHOUSE_OFFICER')

    expect(wrapper.get('a[href="/purchase-orders"]').text()).toContain('Purchase Orders')
    expect(wrapper.get('a[href="/goods-receipts"]').text()).toContain('Goods Receiving')
    expect(wrapper.get('a[href="/inventory"]').text()).toContain('Inventory')
    expect(wrapper.find('a[href="/suppliers"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/quotations"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/email-records"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/products"]').exists()).toBe(false)
  })

  it('shows only workflow and Purchase Order navigation to a Purchase Order Approver', async () => {
    const wrapper = await mountForRole('PURCHASE_ORDER_APPROVER')

    expect(wrapper.get('a[href="/my-tasks"]').text()).toContain('My Tasks')
    expect(wrapper.get('a[href="/purchase-orders"]').text()).toContain('Purchase Orders')
    expect(wrapper.find('a[href="/purchase-requests"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/suppliers"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/quotations"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/goods-receipts"]').exists()).toBe(false)
  })

  it('shows catalog navigation to a catalog manager', async () => {
    const wrapper = await mountForRole('CATALOG_MANAGER')

    expect(wrapper.get('a[href="/product-categories"]').text()).toContain('Product Categories')
    expect(wrapper.get('a[href="/units-of-measure"]').text()).toContain('Units of Measure')
    expect(wrapper.get('a[href="/products"]').text()).toContain('Products')
    expect(wrapper.find('a[href="/suppliers"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/purchase-orders"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/inventory"]').exists()).toBe(false)
  })
})
