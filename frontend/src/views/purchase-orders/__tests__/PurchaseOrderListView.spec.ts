import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia } from 'pinia'

import { flushPromises, mount } from '@vue/test-utils'
import { useAuthStore } from '@/stores/auth'
import { applicationRoles, type ApplicationRole } from '@/types/auth'
import type {
  CreatePurchaseOrderRequest,
  PurchaseOrder,
  PurchaseOrderEmailPreview,
  PurchaseOrderStatus,
  UpdatePurchaseOrderRequest,
} from '@/types/purchaseOrder'
import type { Quotation } from '@/types/quotation'
import PurchaseOrderListView from '../PurchaseOrderListView.vue'

const mocks = vi.hoisted(() => ({
  getPurchaseOrders:
    vi.fn<
      (filters?: { supplierId?: number; status?: PurchaseOrderStatus }) => Promise<PurchaseOrder[]>
    >(),
  create: vi.fn<(payload: CreatePurchaseOrderRequest) => Promise<PurchaseOrder>>(),
  update: vi.fn<(id: number, payload: UpdatePurchaseOrderRequest) => Promise<PurchaseOrder>>(),
  issue: vi.fn<(id: number) => Promise<PurchaseOrder>>(),
  getEmailPreview: vi.fn<(id: number) => Promise<PurchaseOrderEmailPreview>>(),
  retryEmail: vi.fn<(id: number) => Promise<PurchaseOrder>>(),
  cancel: vi.fn<(id: number, reason: string) => Promise<PurchaseOrder>>(),
  delete: vi.fn<(id: number) => Promise<void>>(),
  getQuotations: vi.fn<(filters?: { status?: Quotation['status'] }) => Promise<Quotation[]>>(),
}))

vi.mock('@/services/purchaseOrderService', () => ({
  purchaseOrderService: {
    getAll: mocks.getPurchaseOrders,
    create: mocks.create,
    update: mocks.update,
    issue: mocks.issue,
    getEmailPreview: mocks.getEmailPreview,
    retryEmail: mocks.retryEmail,
    cancel: mocks.cancel,
    delete: mocks.delete,
  },
}))

vi.mock('@/services/quotationService', () => ({
  quotationService: { getAll: mocks.getQuotations },
}))

function quotation(): Quotation {
  return {
    id: 8,
    quotationNumber: 'QT-0008',
    purchaseRequestId: 4,
    purchaseRequestNumber: 'PR-0004',
    supplierId: 2,
    supplierCode: 'SUP-0002',
    supplierName: 'Office Supply Co',
    supplierQuotationReference: 'REF-08',
    quotationDate: '2026-09-15',
    validUntil: '2030-10-15',
    notes: null,
    status: 'Selected',
    totalAmount: 1500,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-15T00:00:00Z',
    updatedAtUtc: null,
    submittedAtUtc: '2026-09-15T01:00:00Z',
    selectedAtUtc: '2026-09-15T02:00:00Z',
    items: [
      {
        id: 12,
        purchaseRequestItemId: 9,
        productId: 3,
        productCode: 'ITEM-0003',
        productName: 'Office Chair',
        unitOfMeasureCode: 'UNIT',
        quantity: 3,
        unitPrice: 500,
        lineTotal: 1500,
      },
    ],
  }
}

function purchaseOrder(status: PurchaseOrderStatus = 'Draft'): PurchaseOrder {
  return {
    id: 5,
    purchaseOrderNumber: 'PO-0005',
    quotationId: 8,
    quotationNumber: 'QT-0008',
    purchaseRequestId: 4,
    purchaseRequestNumber: 'PR-0004',
    supplierId: 2,
    supplierCode: 'SUP-0002',
    supplierName: 'Office Supply Co',
    supplierQuotationReference: 'REF-08',
    orderDate: '2026-09-16',
    expectedDeliveryDate: '2030-09-30',
    deliveryAddress: 'Main warehouse',
    notes: null,
    status,
    totalAmount: 1500,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-16T00:00:00Z',
    updatedAtUtc: null,
    issuedAtUtc: status === 'Draft' ? null : '2026-09-16T01:00:00Z',
    issuedByUserId: status === 'Draft' ? null : 4,
    issuedByName: status === 'Draft' ? null : 'Demo Admin',
    cancelledAtUtc: status === 'Cancelled' ? '2026-09-17T01:00:00Z' : null,
    cancelledByUserId: status === 'Cancelled' ? 4 : null,
    cancelledByName: status === 'Cancelled' ? 'Demo Admin' : null,
    cancellationReason: status === 'Cancelled' ? 'Supplier unavailable' : null,
    emailDelivery: null,
    items: [
      {
        id: 2,
        quotationItemId: 12,
        productId: 3,
        productCode: 'ITEM-0003',
        productName: 'Office Chair',
        unitOfMeasureCode: 'UNIT',
        quantity: 3,
        unitPrice: 500,
        lineTotal: 1500,
      },
    ],
  }
}

function mountView(role: ApplicationRole = applicationRoles.procurementOfficer) {
  const pinia = createPinia()
  const authStore = useAuthStore(pinia)
  authStore.$patch({
    session: {
      accessToken: 'test-token',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      user: {
        id: 5,
        email: 'user@demo.local',
        fullName: 'Demo User',
        departmentId: 1,
        departmentCode: 'PROC',
        departmentName: 'Procurement',
        roles: [role],
      },
    },
  })

  return mount(PurchaseOrderListView, { global: { plugins: [pinia] } })
}

describe('PurchaseOrderListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getPurchaseOrders.mockResolvedValue([purchaseOrder()])
    mocks.getQuotations.mockResolvedValue([quotation()])
    mocks.create.mockResolvedValue(purchaseOrder())
    mocks.update.mockResolvedValue({ ...purchaseOrder(), updatedAtUtc: '2026-09-16T02:00:00Z' })
    mocks.issue.mockResolvedValue(purchaseOrder('Issued'))
    mocks.getEmailPreview.mockResolvedValue({
      id: 1,
      recipientEmail: 'orders@supplier.test',
      subject: 'Purchase Order PO-0005',
      htmlBody: '<h1>PO-0005</h1>',
      status: 'Sent',
    })
    mocks.retryEmail.mockResolvedValue(purchaseOrder('Issued'))
    mocks.cancel.mockResolvedValue(purchaseOrder('Cancelled'))
    mocks.delete.mockResolvedValue(undefined)
    vi.stubGlobal(
      'confirm',
      vi.fn(() => true),
    )
  })

  it('loads selected quotations and shows actions for a draft order', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(mocks.getQuotations).toHaveBeenCalledWith({ status: 'Selected' })
    expect(wrapper.get('h1').text()).toBe('Purchase Orders')
    expect(wrapper.get('tbody').text()).toContain('PO-0005')
    expect(wrapper.get('tbody').text()).toContain('Edit')
    expect(wrapper.get('tbody').text()).toContain('Issue')
    expect(wrapper.get('tbody').text()).toContain('Delete')
    expect(wrapper.get('tbody').text()).not.toContain('Cancel')
  })

  it('issues a draft order, shows a toast, and reloads the register', async () => {
    const wrapper = mountView()
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Issue')
      ?.trigger('click')
    await flushPromises()

    expect(window.confirm).toHaveBeenCalled()
    expect(mocks.issue).toHaveBeenCalledWith(5)
    expect(mocks.getPurchaseOrders).toHaveBeenCalledTimes(2)
    expect(wrapper.get('.success-toast').text()).toContain('PO-0005 was issued')
  })

  it('collects a reason before cancelling an issued order', async () => {
    mocks.getPurchaseOrders.mockResolvedValue([purchaseOrder('Issued')])
    const wrapper = mountView()
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Cancel')
      ?.trigger('click')
    await wrapper.get('#purchase-order-cancel-reason').setValue(' Supplier unavailable ')
    await wrapper.get('.purchase-order-cancel-form').trigger('submit')
    await flushPromises()

    expect(mocks.cancel).toHaveBeenCalledWith(5, 'Supplier unavailable')
    expect(wrapper.find('.purchase-order-cancel-form').exists()).toBe(false)
    expect(wrapper.get('.success-toast').text()).toContain('PO-0005 was cancelled')
  })

  it('does not offer a new order when the selected quotation is already used', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.get('.page-heading .button').attributes('disabled')).toBeDefined()
  })

  it('allows a warehouse officer to review orders without purchasing actions', async () => {
    const wrapper = mountView(applicationRoles.warehouseOfficer)
    await flushPromises()

    expect(mocks.getQuotations).not.toHaveBeenCalled()
    expect(wrapper.get('tbody').text()).toContain('View')
    expect(wrapper.get('tbody').text()).not.toContain('Edit')
    expect(wrapper.get('tbody').text()).not.toContain('Issue')
    expect(wrapper.get('tbody').text()).not.toContain('Delete')
    expect(wrapper.find('.page-heading .button').exists()).toBe(false)
  })

  it('previews and retries a failed supplier email', async () => {
    mocks.getPurchaseOrders.mockResolvedValue([
      {
        ...purchaseOrder('Issued'),
        emailDelivery: {
          id: 1,
          recipientEmail: 'orders@supplier.test',
          subject: 'Purchase Order PO-0005',
          status: 'Failed',
          attemptCount: 1,
          createdAtUtc: '2026-09-16T01:00:00Z',
          lastAttemptAtUtc: '2026-09-16T01:01:00Z',
          sentAtUtc: null,
          lastError: 'SMTP unavailable',
        },
      },
    ])
    const wrapper = mountView()
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Email preview')
      ?.trigger('click')
    await flushPromises()
    expect(mocks.getEmailPreview).toHaveBeenCalledWith(5)
    expect(wrapper.get('#purchase-order-email-preview-title').text()).toContain('PO-0005')

    await wrapper.get('[aria-label="Close preview"]').trigger('click')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Retry email')
      ?.trigger('click')
    await flushPromises()
    expect(mocks.retryEmail).toHaveBeenCalledWith(5)
  })
})
