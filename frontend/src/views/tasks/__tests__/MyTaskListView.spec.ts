import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia } from 'pinia'

import { flushPromises, mount } from '@vue/test-utils'
import PurchaseRequestDetails from '@/components/purchase-requests/PurchaseRequestDetails.vue'
import PurchaseRequestForm from '@/components/purchase-requests/PurchaseRequestForm.vue'
import PurchaseOrderDetails from '@/components/purchase-orders/PurchaseOrderDetails.vue'
import WorkflowActionDialog from '@/components/purchase-requests/WorkflowActionDialog.vue'
import { useAuthStore } from '@/stores/auth'
import type { ApplicationRole } from '@/types/auth'
import type { Product } from '@/types/product'
import type {
  PurchaseRequest,
  PurchaseRequestActionRequest,
  PurchaseRequestFormValues,
} from '@/types/purchaseRequest'
import type { PurchaseOrder, PurchaseOrderFormValues } from '@/types/purchaseOrder'
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
  getPurchaseOrders: vi.fn<() => Promise<PurchaseOrder[]>>(),
  executePurchaseOrderAction:
    vi.fn<
      (
        id: number,
        actionCode: string,
        payload: { comment: string | null },
      ) => Promise<PurchaseOrder>
    >(),
  updatePurchaseOrder:
    vi.fn<
      (id: number, payload: Omit<PurchaseOrderFormValues, 'quotationId'>) => Promise<PurchaseOrder>
    >(),
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

vi.mock('@/services/purchaseOrderService', () => ({
  purchaseOrderService: {
    executeAction: mocks.executePurchaseOrderAction,
    getAll: mocks.getPurchaseOrders,
    update: mocks.updatePurchaseOrder,
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

const pendingPurchaseOrder: PurchaseOrder = {
  id: 7,
  purchaseOrderNumber: 'PO-0007',
  quotationId: 8,
  quotationNumber: 'QT-0008',
  purchaseRequestId: 3,
  purchaseRequestNumber: 'PR-0003',
  supplierId: 2,
  supplierCode: 'SUP-0002',
  supplierName: 'Office Supply Co',
  supplierQuotationReference: 'REF-08',
  orderDate: '2026-09-16',
  expectedDeliveryDate: '2030-09-30',
  deliveryAddress: 'Main warehouse',
  notes: null,
  status: 'PendingApproval',
  totalAmount: 1500,
  createdByUserId: 5,
  createdByName: 'Procurement Officer',
  createdAtUtc: '2026-09-16T00:00:00Z',
  updatedAtUtc: null,
  issuedAtUtc: null,
  issuedByUserId: null,
  issuedByName: null,
  cancelledAtUtc: null,
  cancelledByUserId: null,
  cancelledByName: null,
  cancellationReason: null,
  emailDelivery: null,
  items: [],
  workflow: {
    id: 10,
    templateCode: 'PURCHASE_ORDER',
    templateName: 'Purchase Order Approval',
    templateVersion: 1,
    entityType: 'PurchaseOrder',
    entityId: 7,
    status: 'Running',
    currentStepCode: 'PENDING_APPROVAL',
    currentStepName: 'Pending Approval',
    startedAtUtc: '2026-09-16T00:00:00Z',
    completedAtUtc: null,
    availableActions: [
      {
        code: 'APPROVE',
        name: 'Approve purchase order',
        requiresComment: false,
        toStepCode: 'APPROVED',
        toStepName: 'Approved',
        actioners: [{ actionerType: 'Role', actionerKey: 'PURCHASE_ORDER_APPROVER' }],
      },
      {
        code: 'REJECT',
        name: 'Reject purchase order',
        requiresComment: true,
        toStepCode: 'DRAFT',
        toStepName: 'Draft',
        actioners: [{ actionerType: 'Role', actionerKey: 'PURCHASE_ORDER_APPROVER' }],
      },
    ],
    history: [],
  },
}

describe('MyTaskListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getProducts.mockResolvedValue([])
    mocks.getPurchaseRequests.mockResolvedValue([departmentReviewRequest])
    mocks.getPurchaseOrders.mockResolvedValue([])
    mocks.executePurchaseOrderAction.mockResolvedValue(pendingPurchaseOrder)
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

  function mockTaskLists(count: number) {
    const requests = Array.from({ length: count }, (_, index) => ({
      ...departmentReviewRequest,
      id: index + 1,
      requestNumber: `PR-${String(index + 1).padStart(4, '0')}`,
    }))
    const orders = Array.from({ length: count }, (_, index) => ({
      ...pendingPurchaseOrder,
      id: index + 1,
      purchaseOrderNumber: `PO-${String(index + 1).padStart(4, '0')}`,
    }))
    mocks.getPurchaseRequests.mockResolvedValue(requests)
    mocks.getPurchaseOrders.mockResolvedValue(orders)
    return { requests, orders }
  }

  it.each(['request', 'order'] as const)(
    'paginates %s tasks independently without refetching',
    async (kind) => {
      mockTaskLists(21)
      const wrapper = mountView('ADMIN', 'Demo Admin')
      await flushPromises()
      const panel = wrapper.get(`[aria-labelledby="purchase-${kind}-tasks-title"]`)
      const otherPanel = wrapper.get(
        `[aria-labelledby="purchase-${kind === 'request' ? 'order' : 'request'}-tasks-title"]`,
      )
      const previous = panel.get('nav button:first-child')
      const next = panel.get('nav button:last-child')
      const prefix = kind === 'request' ? 'PR' : 'PO'

      expect(panel.findAll('tbody tr')).toHaveLength(10)
      expect(panel.get('.pagination-summary').text()).toBe('Showing 1–10 of 21 tasks')
      expect(panel.get('nav').text()).toContain('Page 1 of 3')
      expect(previous.attributes('disabled')).toBeDefined()

      await next.trigger('click')
      expect(panel.get('tbody tr').text()).toContain(`${prefix}-0011`)
      expect(panel.get('.pagination-summary').text()).toBe('Showing 11–20 of 21 tasks')
      expect(previous.attributes('disabled')).toBeUndefined()
      expect(otherPanel.get('nav').text()).toContain('Page 1 of 3')
      expect(otherPanel.findAll('tbody tr')).toHaveLength(10)

      await next.trigger('click')
      expect(panel.findAll('tbody tr')).toHaveLength(1)
      expect(panel.get('tbody tr').text()).toContain(`${prefix}-0021`)
      expect(panel.get('nav').text()).toContain('Page 3 of 3')
      expect(next.attributes('disabled')).toBeDefined()

      await previous.trigger('click')
      expect(panel.get('nav').text()).toContain('Page 2 of 3')
      expect(mocks.getPurchaseRequests).toHaveBeenCalledTimes(1)
      expect(mocks.getPurchaseOrders).toHaveBeenCalledTimes(1)
      wrapper.unmount()
    },
  )

  it.each(['request', 'order'] as const)(
    'changes %s page size and returns to its first page',
    async (kind) => {
      mockTaskLists(51)
      const wrapper = mountView('ADMIN', 'Demo Admin')
      await flushPromises()
      const panel = wrapper.get(`[aria-labelledby="purchase-${kind}-tasks-title"]`)
      const otherPanel = wrapper.get(
        `[aria-labelledby="purchase-${kind === 'request' ? 'order' : 'request'}-tasks-title"]`,
      )

      await panel.get('nav button:last-child').trigger('click')
      await panel.get('nav select').setValue('20')
      expect(panel.findAll('tbody tr')).toHaveLength(20)
      expect(panel.get('nav').text()).toContain('Page 1 of 3')
      expect(panel.get('.pagination-summary').text()).toBe('Showing 1–20 of 51 tasks')
      expect(otherPanel.findAll('tbody tr')).toHaveLength(10)
      expect(otherPanel.get<HTMLSelectElement>('nav select').element.value).toBe('10')

      await panel.get('nav button:last-child').trigger('click')
      await panel.get('nav select').setValue('50')
      expect(panel.findAll('tbody tr')).toHaveLength(50)
      expect(panel.get('nav').text()).toContain('Page 1 of 2')
      await panel.get('nav button:last-child').trigger('click')
      expect(panel.findAll('tbody tr')).toHaveLength(1)
      expect(panel.get('.pagination-summary').text()).toBe('Showing 51–51 of 51 tasks')
      wrapper.unmount()
    },
  )

  it.each(['request', 'order'] as const)(
    'filters authorized %s tasks before counting and paginating',
    async (kind) => {
      const { requests, orders } = mockTaskLists(11)
      mocks.getPurchaseRequests.mockResolvedValue([requesterDraft, ...requests])
      mocks.getPurchaseOrders.mockResolvedValue([
        { ...pendingPurchaseOrder, workflow: null },
        ...orders,
      ])
      const wrapper = mountView(
        kind === 'request' ? 'DEPARTMENT_APPROVER' : 'PURCHASE_ORDER_APPROVER',
        'Approver',
      )
      await flushPromises()
      const panel = wrapper.get(`[aria-labelledby="purchase-${kind}-tasks-title"]`)

      expect(panel.get('.panel-toolbar').text()).toContain('11 tasks assigned to your account')
      expect(panel.findAll('tbody tr')).toHaveLength(10)
      expect(panel.get('.pagination-summary').text()).toBe('Showing 1–10 of 11 tasks')
      await panel.get('nav button:last-child').trigger('click')
      expect(panel.findAll('tbody tr')).toHaveLength(1)
      wrapper.unmount()
    },
  )

  it.each(['request', 'order'] as const)(
    'returns to a valid page after completing the last %s task on a page',
    async (kind) => {
      const { requests, orders } = mockTaskLists(11)
      const wrapper = mountView('ADMIN', 'Demo Admin')
      await flushPromises()
      const panel = wrapper.get(`[aria-labelledby="purchase-${kind}-tasks-title"]`)
      const otherPanel = wrapper.get(
        `[aria-labelledby="purchase-${kind === 'request' ? 'order' : 'request'}-tasks-title"]`,
      )
      await panel.get('nav button:last-child').trigger('click')
      await otherPanel.get('nav button:last-child').trigger('click')
      await panel.get('tbody tr button').trigger('click')

      if (kind === 'request') {
        mocks.getPurchaseRequests.mockResolvedValue(requests.slice(0, 10))
        wrapper
          .findComponent(PurchaseRequestDetails)
          .vm.$emit('action', departmentReviewRequest.workflow.availableActions[0]!)
      } else {
        mocks.getPurchaseOrders.mockResolvedValue(orders.slice(0, 10))
        wrapper
          .findComponent(PurchaseOrderDetails)
          .vm.$emit('action', pendingPurchaseOrder.workflow!.availableActions[0]!)
      }
      await flushPromises()
      wrapper.findComponent(WorkflowActionDialog).vm.$emit('execute', null)
      await flushPromises()

      expect(
        kind === 'request' ? mocks.executeAction : mocks.executePurchaseOrderAction,
      ).toHaveBeenCalledWith(11, 'APPROVE', { comment: null })
      expect(panel.get('nav').text()).toContain('Page 1 of 1')
      expect(panel.findAll('tbody tr')).toHaveLength(10)
      expect(panel.get('.pagination-summary').text()).toBe('Showing 1–10 of 10 tasks')
      expect(panel.get('nav button:last-child').attributes('disabled')).toBeDefined()
      expect(otherPanel.get('nav').text()).toContain('Page 2 of 2')
      wrapper.unmount()
    },
  )

  it.each<[ApplicationRole[], boolean, boolean]>([
    [['REQUESTER'], true, false],
    [['DEPARTMENT_APPROVER'], true, false],
    [['FINANCE_APPROVER'], true, false],
    [['PROCUREMENT_OFFICER'], false, true],
    [['PURCHASE_ORDER_APPROVER'], false, true],
    [['ADMIN'], true, true],
    [['WAREHOUSE_OFFICER'], false, false],
    [['REQUESTER', 'PROCUREMENT_OFFICER'], true, true],
    [['WAREHOUSE_OFFICER', 'REQUESTER'], true, false],
  ])(
    'shows and loads the permitted task lists for roles %j',
    async (roles, showRequests, showOrders) => {
      const wrapper = mountView(roles, 'Demo User')
      await flushPromises()

      expect(wrapper.find('#purchase-request-tasks-title').exists()).toBe(showRequests)
      expect(wrapper.find('#purchase-order-tasks-title').exists()).toBe(showOrders)
      expect(mocks.getPurchaseRequests).toHaveBeenCalledTimes(showRequests ? 1 : 0)
      expect(mocks.getPurchaseOrders).toHaveBeenCalledTimes(showOrders ? 1 : 0)
      wrapper.unmount()
    },
  )

  it('shows a loading state for a purchase-order-only account', async () => {
    let resolveOrders!: (orders: PurchaseOrder[]) => void
    mocks.getPurchaseOrders.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveOrders = resolve
      }),
    )
    const wrapper = mountView('PROCUREMENT_OFFICER', 'Procurement Officer')

    expect(wrapper.find('#purchase-request-tasks-title').exists()).toBe(false)
    expect(wrapper.get('.panel-state').text()).toContain('Loading your tasks')
    expect(wrapper.find('nav').exists()).toBe(false)

    resolveOrders([])
    await flushPromises()
    expect(wrapper.get('.panel-state').text()).toContain('No purchase order tasks')
    expect(wrapper.find('nav').exists()).toBe(false)
    wrapper.unmount()
  })

  it('shows purchase order load failures and allows retrying without a request panel', async () => {
    mocks.getPurchaseOrders.mockRejectedValueOnce(new Error('Purchase orders unavailable'))
    const wrapper = mountView('PURCHASE_ORDER_APPROVER', 'Purchase Order Approver')
    await flushPromises()

    expect(wrapper.get('.panel-state-error').text()).toContain('Purchase orders unavailable')
    expect(wrapper.find('nav').exists()).toBe(false)
    expect(wrapper.find('#purchase-request-tasks-title').exists()).toBe(false)

    await wrapper.get('.panel-state-error button').trigger('click')
    await flushPromises()
    expect(mocks.getPurchaseOrders).toHaveBeenCalledTimes(2)
    expect(wrapper.get('.panel-state').text()).toContain('No purchase order tasks')
    wrapper.unmount()
  })

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

  it('allows the Admin-only super admin to manage every assigned workflow task', async () => {
    const wrapper = mountView('ADMIN', 'Demo Admin')
    await flushPromises()

    expect(wrapper.get('tbody tr').text()).toContain('PR-0003')

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
    expect(wrapper.get('.panel-state').text()).toContain('No purchase request tasks')
    expect(wrapper.find('nav').exists()).toBe(false)
    expect(wrapper.get('.success-toast').text()).toContain(
      'PR-0003: Approve department review completed.',
    )
  })

  it('shows an empty state when the current account has no assigned action', async () => {
    const wrapper = mountView('FINANCE_APPROVER', 'Finance Approver')
    await flushPromises()

    expect(wrapper.get('.panel-toolbar').text()).toContain('0 tasks assigned to your account')
    expect(wrapper.get('.panel-state').text()).toContain('No purchase request tasks')
  })

  it('shows purchase order approval tasks to the Purchase Order Approver', async () => {
    mocks.getPurchaseRequests.mockResolvedValue([])
    mocks.getPurchaseOrders.mockResolvedValueOnce([pendingPurchaseOrder]).mockResolvedValue([])
    const wrapper = mountView('PURCHASE_ORDER_APPROVER', 'Purchase Order Approver')
    await flushPromises()

    expect(wrapper.get('#purchase-order-tasks-title').text()).toBe('Purchase Order Tasks')
    expect(wrapper.text()).toContain('PO-0007')

    const detailsButton = wrapper.findAll('button').find((button) => button.text() === 'Details')
    await detailsButton?.trigger('click')
    const approveButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Approve purchase order')
    await approveButton?.trigger('click')
    wrapper.findComponent(WorkflowActionDialog).vm.$emit('execute', null)
    await flushPromises()

    expect(mocks.executePurchaseOrderAction).toHaveBeenCalledWith(7, 'APPROVE', {
      comment: null,
    })
    expect(wrapper.find('nav').exists()).toBe(false)
    expect(wrapper.get('.panel-state').text()).toContain('No purchase order tasks')
  })
})
