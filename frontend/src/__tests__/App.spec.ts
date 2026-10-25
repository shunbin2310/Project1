import { describe, expect, it } from 'vitest'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'

import { flushPromises, mount } from '@vue/test-utils'
import { useAuthStore } from '@/stores/auth'
import type { ApplicationRole } from '@/types/auth'
import LoginView from '@/views/auth/LoginView.vue'
import App from '../App.vue'

async function mountForRole(role: ApplicationRole | ApplicationRole[]) {
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
        roles: Array.isArray(role) ? role : [role],
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
  it.each(['success', 'cancel', 'error', 'redirect'] as const)(
    'clears navigation loading after %s',
    async (outcome) => {
      const wrapper = await mountForRole('ADMIN')
      const router = wrapper.vm.$router
      router.addRoute({ path: '/dashboard', component: { template: '<h1>Dashboard</h1>' } })
      router.addRoute({ path: '/users', component: { template: '<h1>Users</h1>' } })
      let finish!: (result: boolean | string) => void
      let fail!: (error: Error) => void
      const waiting = new Promise<boolean | string>((resolve, reject) => {
        finish = resolve
        fail = reject
      })
      router.beforeResolve((to) => (to.path === '/dashboard' ? waiting : true))

      const navigation = router.push('/dashboard').catch((error: unknown) => error)
      await flushPromises()
      expect(wrapper.get('a[href="/dashboard"]').attributes('aria-busy')).toBe('true')
      expect(wrapper.get('a[href="/purchase-requests"]').attributes('aria-busy')).toBe('false')
      expect(wrapper.get('[role="status"]').text()).toBe('Loading page…')

      if (outcome === 'error') fail(new Error('Unable to load page.'))
      else finish(outcome === 'redirect' ? '/users' : outcome === 'success')
      await navigation
      await flushPromises()

      expect(wrapper.find('a[aria-busy="true"]').exists()).toBe(false)
      expect(wrapper.get('[role="status"]').text()).toBe('')
      expect(router.currentRoute.value.path).toBe(
        outcome === 'success'
          ? '/dashboard'
          : outcome === 'redirect'
            ? '/users'
            : '/purchase-requests',
      )
      wrapper.unmount()
    },
  )

  it('keeps the latest navigation loading when an older navigation is cancelled', async () => {
    const wrapper = await mountForRole('ADMIN')
    const router = wrapper.vm.$router
    router.addRoute({ path: '/dashboard', component: { template: '<h1>Dashboard</h1>' } })
    router.addRoute({ path: '/users', component: { template: '<h1>Users</h1>' } })
    let finishDashboard!: (result: boolean) => void
    let finishUsers!: (result: boolean) => void
    const dashboard = new Promise<boolean>((resolve) => {
      finishDashboard = resolve
    })
    const users = new Promise<boolean>((resolve) => {
      finishUsers = resolve
    })
    router.beforeResolve((to) => (to.path === '/dashboard' ? dashboard : users))

    const first = router.push('/dashboard')
    await flushPromises()
    const second = router.push('/users')
    await flushPromises()
    finishDashboard(true)
    await first
    await flushPromises()
    expect(wrapper.get('a[href="/users"]').attributes('aria-busy')).toBe('true')
    expect(wrapper.get('a[href="/dashboard"]').attributes('aria-busy')).toBe('false')

    finishUsers(true)
    await second
    await flushPromises()
    expect(wrapper.find('a[aria-busy="true"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it.each<[ApplicationRole[], boolean]>([
    [['REQUESTER'], true],
    [['DEPARTMENT_APPROVER'], true],
    [['FINANCE_APPROVER'], true],
    [['PROCUREMENT_OFFICER'], true],
    [['PURCHASE_ORDER_APPROVER'], true],
    [['ADMIN'], true],
    [['WAREHOUSE_OFFICER'], false],
    [['WAREHOUSE_OFFICER', 'REQUESTER'], true],
  ])('shows My Tasks for roles %j: %s', async (roles, visible) => {
    const wrapper = await mountForRole(roles)

    expect(wrapper.find('a[href="/my-tasks"]').exists()).toBe(visible)
    expect(wrapper.findAll('.primary-nav p').some((label) => label.text() === 'Tasks')).toBe(
      visible,
    )
    wrapper.unmount()
  })

  it('renders the brand and welcome heading on the login page', async () => {
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
    expect(wrapper.get('#login-title').text()).toBe('Welcome to Project1 Inventory')
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
    expect(wrapper.find('a[href="/product-categories"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/units-of-measure"]').exists()).toBe(false)
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
    expect(wrapper.get('a[href="/products"]').text()).toContain('Products')
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
})
