import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia } from 'pinia'

import { flushPromises, mount } from '@vue/test-utils'
import PurchaseRequestDetails from '@/components/purchase-requests/PurchaseRequestDetails.vue'
import PurchaseRequestForm from '@/components/purchase-requests/PurchaseRequestForm.vue'
import WorkflowActionDialog from '@/components/purchase-requests/WorkflowActionDialog.vue'
import { useAuthStore } from '@/stores/auth'
import type { ApplicationRole } from '@/types/auth'
import type { Product } from '@/types/product'
import type {
  PurchaseRequest,
  PurchaseRequestActionRequest,
  PurchaseRequestFormValues,
} from '@/types/purchaseRequest'
import MyTaskListView from '../MyTaskListView.vue'

const mocks = vi.hoisted(() => ({
  executeAction:
    vi.fn<
      (
        id: number,
        actionCode: string,
        payload: PurchaseRequestActionRequest,
      ) => Promise<PurchaseRequest>
    >(),
  getProducts: vi.fn<(includeInactive?: boolean) => Promise<Product[]>>(),
  getPurchaseRequests: vi.fn<() => Promise<PurchaseRequest[]>>(),
  updatePurchaseRequest:
    vi.fn<(id: number, payload: PurchaseRequestFormValues) => Promise<PurchaseRequest>>(),
}))

vi.mock('@/services/productService', () => ({
  productService: { getAll: mocks.getProducts },
}))

vi.mock('@/services/purchaseRequestService', () => ({
  purchaseRequestService: {
    executeAction: mocks.executeAction,
    getAll: mocks.getPurchaseRequests,
    update: mocks.updatePurchaseRequest,
  },
}))

const departmentReviewRequest: PurchaseRequest = {
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
    history: [],
  },
}

const requesterDraft: PurchaseRequest = {
  ...departmentReviewRequest,
  workflow: {
    ...departmentReviewRequest.workflow,
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

describe('MyTaskListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getProducts.mockResolvedValue([])
    mocks.getPurchaseRequests.mockResolvedValue([departmentReviewRequest])
    mocks.executeAction.mockResolvedValue(departmentReviewRequest)
    mocks.updatePurchaseRequest.mockResolvedValue(requesterDraft)
  })

  function mountView(roleOrRoles: ApplicationRole | ApplicationRole[], fullName: string) {
    const roles = Array.isArray(roleOrRoles) ? roleOrRoles : [roleOrRoles]
    const pinia = createPinia()
    const authStore = useAuthStore(pinia)
    authStore.$patch({
      session: {
        accessToken: 'test-token',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        user: {
          id: roles.includes('ADMIN') ? 4 : roles.includes('REQUESTER') ? 1 : 2,
          email: 'user@demo.local',
          fullName,
          departmentId: 1,
          departmentCode: 'IT',
          departmentName: 'Information Technology',
          roles,
        },
      },
    })

    return mount(MyTaskListView, {
      global: {
        plugins: [pinia],
      },
    })
  }

  it('shows only workflow tasks assigned to the signed-in approver', async () => {
    const wrapper = mountView('DEPARTMENT_APPROVER', 'Department Approver')
    await flushPromises()

    expect(wrapper.get('h1').text()).toBe('My Tasks')
    expect(wrapper.get('.panel-toolbar').text()).toContain('1 task assigned to your account')
    expect(wrapper.get('tbody tr').text()).toContain('PR-0003')
    expect(wrapper.findAll('th').some((header) => header.text() === 'Available actions')).toBe(
      false,
    )

    const detailsButton = wrapper.findAll('button').find((button) => button.text() === 'Details')
    await detailsButton?.trigger('click')

    expect(wrapper.get('.workflow-actions-panel').text()).toContain(
      'Current identity: Department Approver',
    )
    expect(wrapper.findComponent(PurchaseRequestDetails).exists()).toBe(true)
  })

  it('matches a requester task by the authenticated user id', async () => {
    mocks.getPurchaseRequests.mockResolvedValue([requesterDraft])
    const wrapper = mountView('REQUESTER', 'Demo Requester')
    await flushPromises()

    expect(wrapper.get('.panel-toolbar').text()).toContain('1 task assigned to your account')
    expect(wrapper.get('tbody tr').text()).toContain('DRAFT')
    expect(wrapper.text()).not.toContain('New purchase request')
    expect(wrapper.findAll('button').some((button) => button.text() === 'Details')).toBe(false)

    const editButton = wrapper.findAll('button').find((button) => button.text() === 'Edit')
    await editButton?.trigger('click')

    expect(wrapper.findComponent(PurchaseRequestForm).exists()).toBe(true)
    expect(wrapper.get('[role="dialog"]').text()).toContain('Edit PR-0003')
  })

  it('allows the multi-role admin to review approvals without editing another requester draft', async () => {
    const wrapper = mountView(
      ['ADMIN', 'REQUESTER', 'DEPARTMENT_APPROVER', 'FINANCE_APPROVER'],
      'Demo Admin',
    )
    await flushPromises()

    expect(wrapper.get('tbody tr').text()).toContain('PR-0003')
    expect(wrapper.findAll('button').some((button) => button.text() === 'Edit')).toBe(false)

    const detailsButton = wrapper.findAll('button').find((button) => button.text() === 'Details')
    await detailsButton?.trigger('click')

    const approveButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Approve department review')
    expect(approveButton?.attributes('disabled')).toBeUndefined()
  })

  it('updates an owned draft from the My Tasks edit form', async () => {
    mocks.getPurchaseRequests.mockResolvedValue([requesterDraft])
    const wrapper = mountView('REQUESTER', 'Demo Requester')
    await flushPromises()
    const values: PurchaseRequestFormValues = {
      requiredDate: '2031-01-15',
      justification: 'Updated equipment request',
      items: [],
    }

    const editButton = wrapper.findAll('button').find((button) => button.text() === 'Edit')
    await editButton?.trigger('click')
    wrapper.findComponent(PurchaseRequestForm).vm.$emit('save', values, false)
    await flushPromises()

    expect(mocks.updatePurchaseRequest).toHaveBeenCalledWith(3, values)
    expect(wrapper.findComponent(PurchaseRequestForm).exists()).toBe(false)
    expect(wrapper.get('.success-toast').text()).toContain('PR-0003 draft was updated.')
  })

  it('updates and submits an owned draft directly from the edit form', async () => {
    mocks.getPurchaseRequests.mockResolvedValue([requesterDraft])
    mocks.executeAction.mockResolvedValue({
      ...requesterDraft,
      workflow: {
        ...requesterDraft.workflow,
        currentStepCode: 'DEPARTMENT_REVIEW',
        currentStepName: 'Department Review',
        availableActions: [],
      },
    })
    const wrapper = mountView('REQUESTER', 'Demo Requester')
    await flushPromises()
    const values: PurchaseRequestFormValues = {
      requiredDate: '2031-01-15',
      justification: 'Ready for review',
      items: [],
    }

    const editButton = wrapper.findAll('button').find((button) => button.text() === 'Edit')
    await editButton?.trigger('click')
    wrapper.findComponent(PurchaseRequestForm).vm.$emit('save', values, true)
    await flushPromises()

    expect(mocks.updatePurchaseRequest).toHaveBeenCalledWith(3, values)
    expect(mocks.executeAction).toHaveBeenCalledWith(3, 'SUBMIT', {
      comment: 'Updated and submitted from My Tasks.',
    })
    expect(wrapper.findComponent(PurchaseRequestForm).exists()).toBe(false)
    expect(wrapper.get('.success-toast').text()).toContain(
      'PR-0003 was submitted for department review.',
    )
  })

  it('closes the dialogs, removes the completed task, and shows a success toast', async () => {
    mocks.getPurchaseRequests
      .mockResolvedValueOnce([departmentReviewRequest])
      .mockResolvedValueOnce([])
    const wrapper = mountView('DEPARTMENT_APPROVER', 'Department Approver')
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
    expect(wrapper.find('tbody').exists()).toBe(false)
    expect(wrapper.get('.panel-state').text()).toContain("You're all caught up")
    expect(wrapper.get('.success-toast').text()).toContain(
      'PR-0003: Approve department review completed.',
    )
  })

  it('shows an empty state when the current account has no assigned action', async () => {
    const wrapper = mountView('FINANCE_APPROVER', 'Finance Approver')
    await flushPromises()

    expect(wrapper.get('.panel-toolbar').text()).toContain('0 tasks assigned to your account')
    expect(wrapper.get('.panel-state').text()).toContain("You're all caught up")
  })
})
