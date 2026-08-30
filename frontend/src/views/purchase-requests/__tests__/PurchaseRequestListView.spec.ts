import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import PurchaseRequestForm from '@/components/purchase-requests/PurchaseRequestForm.vue'
import type { Department } from '@/types/department'
import type { Product } from '@/types/product'
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
  getDepartments: vi.fn<(includeInactive?: boolean) => Promise<Department[]>>(),
  getProducts: vi.fn<(includeInactive?: boolean) => Promise<Product[]>>(),
  getPurchaseRequests: vi.fn<(stepCode?: string) => Promise<PurchaseRequest[]>>(),
  updatePurchaseRequest:
    vi.fn<(id: number, payload: PurchaseRequestFormValues) => Promise<PurchaseRequest>>(),
}))

vi.mock('@/services/departmentService', () => ({
  departmentService: { getAll: mocks.getDepartments },
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
        actioners: [{ actionerType: 'Requester', actionerKey: 'Tester 2' }],
      },
    ],
  },
}

describe('PurchaseRequestListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getDepartments.mockResolvedValue([])
    mocks.getProducts.mockResolvedValue([])
    mocks.getPurchaseRequests.mockResolvedValue([purchaseRequest])
    mocks.createPurchaseRequest.mockResolvedValue(draftPurchaseRequest)
    mocks.executeAction.mockResolvedValue(purchaseRequest)
  })

  it('keeps the selected approver identity when details are opened', async () => {
    const wrapper = mount(PurchaseRequestListView)
    await flushPromises()

    await wrapper.get('#workflow-identity').setValue('department-approver')
    const detailsButton = wrapper.findAll('button').find((button) => button.text() === 'Details')
    await detailsButton?.trigger('click')

    expect(wrapper.get('.workflow-actions-panel').text()).toContain(
      'Current identity: Department Manager',
    )
    const approveButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Approve department review')
    expect(approveButton?.attributes('disabled')).toBeUndefined()
  })

  it('creates a draft and immediately submits it when requested by the form', async () => {
    const wrapper = mount(PurchaseRequestListView)
    await flushPromises()
    const values = {
      requesterName: 'Tester 2',
      departmentId: 1,
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
      actionBy: 'Tester 2',
      actorRoles: [],
      comment: 'Created and submitted from the form.',
    })
  })
})
