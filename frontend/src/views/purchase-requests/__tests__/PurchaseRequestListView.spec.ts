import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'

import { flushPromises, mount } from '@vue/test-utils'
import PurchaseRequestDetails from '@/components/purchase-requests/PurchaseRequestDetails.vue'
import PurchaseRequestForm from '@/components/purchase-requests/PurchaseRequestForm.vue'
import WorkflowActionDialog from '@/components/purchase-requests/WorkflowActionDialog.vue'
import { useAuthStore } from '@/stores/auth'
import type { Product } from '@/types/product'
import type { ApplicationRole } from '@/types/auth'
import type {
  PurchaseRequest,
  PurchaseRequestActionRequest,
  PurchaseRequestFormValues,
} from '@/types/purchaseRequest'
import PurchaseRequestListView from '../PurchaseRequestListView.vue'

const mocks = vi.hoisted(() => ({
  createPurchaseRequest: vi.fn<(payload: PurchaseRequestFormValues) => Promise<PurchaseRequest>>(),
  deletePurchaseRequest: vi.fn<(id: number) => Promise<void>>(),
  executeAction:
    vi.fn<
      (
        id: number,
        actionCode: string,
        payload: PurchaseRequestActionRequest,
      ) => Promise<PurchaseRequest>
    >(),
  getProducts: vi.fn<(includeInactive?: boolean) => Promise<Product[]>>(),
  getPurchaseRequests: vi.fn<(stepCode?: string) => Promise<PurchaseRequest[]>>(),
  updatePurchaseRequest:
    vi.fn<(id: number, payload: PurchaseRequestFormValues) => Promise<PurchaseRequest>>(),
}))

vi.mock('@/services/productService', () => ({
  productService: { getAll: mocks.getProducts },
}))

vi.mock('@/services/purchaseRequestService', () => ({
  purchaseRequestService: {
    create: mocks.createPurchaseRequest,
    delete: mocks.deletePurchaseRequest,
    executeAction: mocks.executeAction,
    getAll: mocks.getPurchaseRequests,
    update: mocks.updatePurchaseRequest,
  },
}))

const purchaseRequest: PurchaseRequest = {
  id: 3,
  requestNumber: 'PR-0003',
  requesterName: 'Tester 2',
  departmentId: 1,
  departmentCode: 'IT',
  departmentName: 'Information Technology',
  requiredDate: '2030-12-31',
  justification: 'New equipment',
  estimatedTotal: 2399.9,
  createdAtUtc: '2026-08-30T00:00:00Z',
  updatedAtUtc: null,
  items: [],
  workflow: {
    id: 1,
    templateCode: 'PURCHASE_REQUEST',
    templateName: 'Purchase Request Approval',
    templateVersion: 1,
    entityType: 'PurchaseRequest',
    entityId: 3,
    status: 'Running',
    currentStepCode: 'DEPARTMENT_REVIEW',
    currentStepName: 'Department Review',
    startedAtUtc: '2026-08-30T00:00:00Z',
    completedAtUtc: null,
    availableActions: [
      {
        code: 'APPROVE',
        name: 'Approve department review',
        requiresComment: false,
        toStepCode: 'FINANCE_REVIEW',
        toStepName: 'Finance Review',
        actioners: [{ actionerType: 'Role', actionerKey: 'DEPARTMENT_APPROVER' }],
      },
    ],
    history: [
      {
        id: 1,
        fromStepCode: null,
        toStepCode: 'DRAFT',
        actionCode: 'START',
        actionBy: 'Tester 2',
        comment: null,
        actionAtUtc: '2026-08-30T00:00:00Z',
      },
    ],
  },
}

const draftPurchaseRequest: PurchaseRequest = {
  ...purchaseRequest,
  workflow: {
    ...purchaseRequest.workflow,
    currentStepCode: 'DRAFT',
    currentStepName: 'Draft',
    availableActions: [
      {
        code: 'SUBMIT',
        name: 'Submit',
        requiresComment: false,
        toStepCode: 'DEPARTMENT_REVIEW',
        toStepName: 'Department Review',
        actioners: [{ actionerType: 'Requester', actionerKey: '1' }],
      },
    ],
  },
}

describe('PurchaseRequestListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getProducts.mockResolvedValue([])
    mocks.getPurchaseRequests.mockResolvedValue([purchaseRequest])
    mocks.createPurchaseRequest.mockResolvedValue(draftPurchaseRequest)
    mocks.executeAction.mockResolvedValue(purchaseRequest)
  })

  async function mountView(
    role: ApplicationRole,
    fullName: string,
    initialPath = '/purchase-requests',
  ) {
    const pinia = createPinia()
    const authStore = useAuthStore(pinia)
    authStore.$patch({
      session: {
        accessToken: 'test-token',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        user: {
          id: role === 'REQUESTER' ? 1 : 2,
          email: 'user@demo.local',
          fullName,
          departmentId: 1,
          departmentCode: 'IT',
          departmentName: 'Information Technology',
          roles: [role],
        },
      },
    })

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: '/purchase-requests',
          name: 'purchase-requests',
          component: { template: '<div />' },
        },
      ],
    })
    await router.push(initialPath)
    await router.isReady()

    return mount(PurchaseRequestListView, { global: { plugins: [pinia, router] } })
  }

  it('uses the authenticated approver identity for workflow actions', async () => {
    const wrapper = await mountView('DEPARTMENT_APPROVER', 'Department Approver')
    await flushPromises()

    expect(wrapper.text()).not.toContain('Signed in as')

    const detailsButton = wrapper.findAll('button').find((button) => button.text() === 'Details')
    await detailsButton?.trigger('click')

    expect(wrapper.get('.workflow-actions-panel').text()).toContain(
      'Current identity: Department Approver',
    )
    const approveButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Approve department review')
    expect(approveButton?.attributes('disabled')).toBeUndefined()
  })

  it('shows only Edit for a draft assigned to the signed-in requester', async () => {
    mocks.getPurchaseRequests.mockResolvedValue([draftPurchaseRequest])
    const wrapper = await mountView('REQUESTER', 'Demo Requester')
    await flushPromises()

    expect(wrapper.findAll('button').some((button) => button.text() === 'Details')).toBe(false)
    const editButton = wrapper.findAll('button').find((button) => button.text() === 'Edit')
    await editButton?.trigger('click')

    expect(wrapper.findComponent(PurchaseRequestForm).exists()).toBe(true)
    expect(wrapper.get('[role="dialog"]').text()).toContain('Edit PR-0003')
  })

  it('shows read-only Details without draft actions for a draft owned by another user', async () => {
    mocks.getPurchaseRequests.mockResolvedValue([draftPurchaseRequest])
    const wrapper = await mountView('ADMIN', 'Demo Admin')
    await flushPromises()

    expect(wrapper.findAll('button').some((button) => button.text() === 'Edit')).toBe(false)
    const detailsButton = wrapper.findAll('button').find((button) => button.text() === 'Details')
    await detailsButton?.trigger('click')

    expect(wrapper.findComponent(PurchaseRequestDetails).exists()).toBe(true)
    expect(wrapper.find('.workflow-actions-panel').exists()).toBe(false)
  })

  it('closes workflow dialogs and shows a toast after an action succeeds', async () => {
    const wrapper = await mountView('DEPARTMENT_APPROVER', 'Department Approver')
    await flushPromises()

    const detailsButton = wrapper.findAll('button').find((button) => button.text() === 'Details')
    await detailsButton?.trigger('click')
    const approveButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Approve department review')
    await approveButton?.trigger('click')
    wrapper.findComponent(WorkflowActionDialog).vm.$emit('execute', 'Approved for testing.')
    await flushPromises()

    expect(mocks.executeAction).toHaveBeenCalledWith(3, 'APPROVE', {
      comment: 'Approved for testing.',
    })
    expect(wrapper.findComponent(WorkflowActionDialog).exists()).toBe(false)
    expect(wrapper.findComponent(PurchaseRequestDetails).exists()).toBe(false)
    expect(wrapper.get('.success-toast').text()).toContain(
      'PR-0003: Approve department review completed.',
    )
  })

  it('creates a draft and immediately submits it when requested by the form', async () => {
    const wrapper = await mountView('REQUESTER', 'Demo Requester')
    await flushPromises()
    const values = {
      requiredDate: '2030-12-31',
      justification: 'New equipment',
      items: [{ productId: 1, quantity: 1, estimatedUnitPrice: 100 }],
    }

    const newButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('New purchase request'))
    await newButton?.trigger('click')
    wrapper.findComponent(PurchaseRequestForm).vm.$emit('save', values, true)
    await flushPromises()

    expect(mocks.createPurchaseRequest).toHaveBeenCalledWith(values)
    expect(mocks.executeAction).toHaveBeenCalledWith(3, 'SUBMIT', {
      comment: 'Created and submitted from the form.',
    })
    expect(wrapper.findComponent(PurchaseRequestForm).exists()).toBe(false)
    expect(wrapper.get('.success-toast').text()).toContain(
      'PR-0003 was submitted for department review.',
    )
  })

  it('opens the create form from the My Tasks shortcut query', async () => {
    const wrapper = await mountView('REQUESTER', 'Demo Requester', '/purchase-requests?create=true')
    await flushPromises()

    expect(wrapper.findComponent(PurchaseRequestForm).exists()).toBe(true)
  })
})
