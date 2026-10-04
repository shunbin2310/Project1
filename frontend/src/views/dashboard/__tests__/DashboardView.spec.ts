import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'

import { flushPromises, mount } from '@vue/test-utils'
import { useAuthStore } from '@/stores/auth'
import type { Dashboard } from '@/types/dashboard'
import DashboardView from '../DashboardView.vue'

const mocks = vi.hoisted(() => ({
  get: vi.fn<() => Promise<Dashboard>>(),
}))

vi.mock('@/services/dashboardService', () => ({
  dashboardService: { get: mocks.get },
}))

const populatedDashboard: Dashboard = {
  summaryCards: [
    {
      key: 'department-review',
      label: 'Department review',
      value: 3,
      description: 'Requests waiting for approval.',
      route: '/my-tasks',
      tone: 'warning',
    },
  ],
  reminders: [
    {
      key: 'department-review',
      title: 'Department approvals waiting',
      description: 'Review each request.',
      count: 3,
      route: '/my-tasks',
      severity: 'warning',
    },
  ],
  recentActivity: [
    {
      key: 'workflow:PurchaseRequest:9',
      module: 'Purchase Request',
      reference: 'PR-0009',
      title: 'Submitted for approval',
      description: 'Draft → Department Review.',
      actorName: 'Demo Requester',
      occurredAtUtc: '2026-10-01T08:30:00Z',
      route: '/purchase-requests',
    },
  ],
  generatedAtUtc: '2026-10-01T09:00:00Z',
}

describe('DashboardView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.get.mockResolvedValue(populatedDashboard)
  })

  async function mountView() {
    const pinia = createPinia()
    const authStore = useAuthStore(pinia)
    authStore.$patch({
      session: {
        accessToken: 'test-token',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        user: {
          id: 2,
          email: 'department@demo.local',
          fullName: 'Department Approver',
          departmentId: 1,
          departmentCode: 'IT',
          departmentName: 'Information Technology',
          roles: ['DEPARTMENT_APPROVER'],
        },
      },
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/dashboard', component: DashboardView },
        { path: '/my-tasks', component: { template: '<div>Tasks</div>' } },
        { path: '/purchase-requests', component: { template: '<div>Requests</div>' } },
      ],
    })
    await router.push('/dashboard')
    await router.isReady()

    return mount(DashboardView, { global: { plugins: [pinia, router] } })
  }

  it('shows role-aware cards, reminders, activity, and links', async () => {
    const wrapper = await mountView()
    await flushPromises()

    expect(mocks.get).toHaveBeenCalledOnce()
    expect(wrapper.get('h1').text()).toContain('Department')
    expect(wrapper.get('.dashboard-summary-card').text()).toContain('3')
    expect(wrapper.get('.dashboard-reminder').text()).toContain('Department approvals waiting')
    expect(wrapper.get('.dashboard-activity-list').text()).toContain('PR-0009')
    expect(wrapper.get('.dashboard-summary-card').attributes('href')).toBe('/my-tasks')
    expect(wrapper.get('.dashboard-activity-link').attributes('href')).toBe('/purchase-requests')
  })

  it('shows helpful empty states when there are no reminders or activity', async () => {
    mocks.get.mockResolvedValue({
      summaryCards: [],
      reminders: [],
      recentActivity: [],
      generatedAtUtc: '2026-10-01T09:00:00Z',
    })
    const wrapper = await mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Nothing needs attention')
    expect(wrapper.text()).toContain('No recent activity')
  })

  it('shows an error and retries loading', async () => {
    mocks.get.mockRejectedValueOnce(new Error('Dashboard API unavailable'))
    const wrapper = await mountView()
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('Dashboard API unavailable')

    mocks.get.mockResolvedValueOnce(populatedDashboard)
    await wrapper.get('[role="alert"] button').trigger('click')
    await flushPromises()

    expect(mocks.get).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Department approvals waiting')
  })
})
