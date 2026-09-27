import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia } from 'pinia'

import { flushPromises, mount } from '@vue/test-utils'
import { useAuthStore } from '@/stores/auth'
import { applicationRoles, type ApplicationRole } from '@/types/auth'
import type {
  CreateGoodsReceiptRequest,
  GoodsReceipt,
  GoodsReceiptStatus,
  UpdateGoodsReceiptRequest,
} from '@/types/goodsReceipt'
import type { PurchaseOrder, PurchaseOrderStatus } from '@/types/purchaseOrder'
import GoodsReceiptListView from '../GoodsReceiptListView.vue'

const mocks = vi.hoisted(() => ({
  getReceipts:
    vi.fn<
      (filters?: {
        purchaseOrderId?: number
        supplierId?: number
        status?: GoodsReceiptStatus
      }) => Promise<GoodsReceipt[]>
    >(),
  create: vi.fn<(payload: CreateGoodsReceiptRequest) => Promise<GoodsReceipt>>(),
  update: vi.fn<(id: number, payload: UpdateGoodsReceiptRequest) => Promise<GoodsReceipt>>(),
  post: vi.fn<(id: number) => Promise<GoodsReceipt>>(),
  delete: vi.fn<(id: number) => Promise<void>>(),
  getPurchaseOrders:
    vi.fn<
      (filters?: { supplierId?: number; status?: PurchaseOrderStatus }) => Promise<PurchaseOrder[]>
    >(),
}))

vi.mock('@/services/goodsReceiptService', () => ({
  goodsReceiptService: {
    getAll: mocks.getReceipts,
    create: mocks.create,
    update: mocks.update,
    post: mocks.post,
    delete: mocks.delete,
  },
}))

vi.mock('@/services/purchaseOrderService', () => ({
  purchaseOrderService: { getAll: mocks.getPurchaseOrders },
}))

function purchaseOrder(status: PurchaseOrderStatus = 'Issued'): PurchaseOrder {
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
    orderDate: '2026-09-15',
    expectedDeliveryDate: '2026-09-30',
    deliveryAddress: 'Main warehouse',
    notes: null,
    status,
    totalAmount: 1500,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-15T00:00:00Z',
    updatedAtUtc: null,
    issuedAtUtc: '2026-09-15T01:00:00Z',
    issuedByUserId: 4,
    issuedByName: 'Demo Admin',
    cancelledAtUtc: null,
    cancelledByUserId: null,
    cancelledByName: null,
    cancellationReason: null,
    items: [
      {
        id: 2,
        quotationItemId: 12,
        productId: 3,
        productCode: 'ITEM-0003',
        productName: 'Office Chair',
        unitOfMeasureCode: 'UNIT',
        quantity: 10,
        unitPrice: 150,
        lineTotal: 1500,
      },
    ],
  }
}

function receipt(status: GoodsReceiptStatus = 'Draft', id = 9): GoodsReceipt {
  return {
    id,
    goodsReceiptNumber: `GR-${String(id).padStart(4, '0')}`,
    purchaseOrderId: 5,
    purchaseOrderNumber: 'PO-0005',
    supplierId: 2,
    supplierCode: 'SUP-0002',
    supplierName: 'Office Supply Co',
    supplierDeliveryNoteNumber: `DN-${id}`,
    receivedDate: '2026-09-16',
    notes: null,
    status,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-16T00:00:00Z',
    updatedAtUtc: null,
    postedAtUtc: status === 'Posted' ? '2026-09-16T01:00:00Z' : null,
    postedByUserId: status === 'Posted' ? 4 : null,
    postedByName: status === 'Posted' ? 'Demo Admin' : null,
    items: [
      {
        id,
        purchaseOrderItemId: 2,
        productId: 3,
        productCode: 'ITEM-0003',
        productName: 'Office Chair',
        unitOfMeasureCode: 'UNIT',
        orderedQuantity: 10,
        quantityReceived: 4,
      },
    ],
  }
}

function mountView(role: ApplicationRole = applicationRoles.warehouseOfficer) {
  const pinia = createPinia()
  const authStore = useAuthStore(pinia)
  authStore.$patch({
    session: {
      accessToken: 'test-token',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      user: {
        id: 6,
        email: 'user@demo.local',
        fullName: 'Demo User',
        departmentId: 1,
        departmentCode: 'WH',
        departmentName: 'Warehouse',
        roles: [role],
      },
    },
  })

  return mount(GoodsReceiptListView, { global: { plugins: [pinia] } })
}

describe('GoodsReceiptListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getReceipts.mockResolvedValue([receipt()])
    mocks.getPurchaseOrders.mockResolvedValue([purchaseOrder()])
    mocks.create.mockResolvedValue(receipt())
    mocks.update.mockResolvedValue({ ...receipt(), updatedAtUtc: '2026-09-16T02:00:00Z' })
    mocks.post.mockResolvedValue(receipt('Posted'))
    mocks.delete.mockResolvedValue(undefined)
    vi.stubGlobal(
      'confirm',
      vi.fn(() => true),
    )
  })

  it('loads receipts and shows draft actions', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(mocks.getReceipts).toHaveBeenCalled()
    expect(mocks.getPurchaseOrders).toHaveBeenCalled()
    expect(wrapper.get('h1').text()).toBe('Goods Receiving')
    expect(wrapper.get('tbody').text()).toContain('GR-0009')
    expect(wrapper.get('tbody').text()).toContain('Edit')
    expect(wrapper.get('tbody').text()).toContain('Post')
    expect(wrapper.get('tbody').text()).toContain('Delete')
    expect(wrapper.get('.page-heading .button').attributes('disabled')).toBeDefined()
  })

  it('posts a draft, shows a toast, and reloads the register', async () => {
    const wrapper = mountView()
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Post')
      ?.trigger('click')
    await flushPromises()

    expect(window.confirm).toHaveBeenCalled()
    expect(mocks.post).toHaveBeenCalledWith(9)
    expect(mocks.getReceipts).toHaveBeenCalledTimes(2)
    expect(wrapper.get('.success-toast').text()).toContain('GR-0009 was posted')
  })

  it('uses posted receipts to calculate previous and remaining quantities', async () => {
    mocks.getReceipts.mockResolvedValue([receipt('Posted')])
    mocks.getPurchaseOrders.mockResolvedValue([purchaseOrder('PartiallyReceived')])
    const wrapper = mountView()
    await flushPromises()

    await wrapper.get('.page-heading .button').trigger('click')

    const formText = wrapper.get('.goods-receipt-form').text().replace(/\s+/g, ' ')
    expect(formText).toContain('Previously received')
    expect(formText).toContain('4')
    expect(formText).toContain('6')
  })

  it('allows procurement to review receipts without warehouse actions', async () => {
    const wrapper = mountView(applicationRoles.procurementOfficer)
    await flushPromises()

    expect(wrapper.get('tbody').text()).toContain('View')
    expect(wrapper.get('tbody').text()).not.toContain('Edit')
    expect(wrapper.get('tbody').text()).not.toContain('Post')
    expect(wrapper.get('tbody').text()).not.toContain('Delete')
    expect(wrapper.find('.page-heading .button').exists()).toBe(false)
  })
})
