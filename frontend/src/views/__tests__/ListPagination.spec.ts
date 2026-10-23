import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import type { Component } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { useAuthStore } from '@/stores/auth'

const views = import.meta.glob<{ default: Component }>('../**/*ListView.vue')
const cases = [
  ['departments/DepartmentListView', 'records'],
  ['email-records/EmailRecordListView', 'records'],
  ['email-templates/EmailTemplateListView', 'versions'],
  ['goods-receipts/GoodsReceiptListView', 'records'],
  ['inventory/InventoryListView', 'products'],
  ['products/ProductListView', 'records'],
  ['purchase-orders/PurchaseOrderListView', 'records'],
  ['purchase-requests/PurchaseRequestListView', 'records'],
  ['quotations/QuotationListView', 'records'],
  ['supplier-products/SupplierProductListView', 'relationships'],
  ['suppliers/SupplierListView', 'records'],
  ['users/UserListView', 'records'],
  ['workflow-templates/WorkflowTemplateListView', 'versions'],
] as const

// Each endpoint uses the same identifiers so pagination can be exercised through every real view.
function record(id: number) {
  const name = `Record ${String(id).padStart(4, '0')}`
  return {
    id,
    code: name,
    name,
    description: null,
    isActive: true,
    email: `${id}@example.test`,
    fullName: name,
    roles: ['REQUESTER'],
    departmentId: 1,
    departmentCode: 'IT',
    departmentName: 'Information Technology',
    productId: id,
    productCode: name,
    productName: name,
    defaultUnitPrice: 100,
    productDefaultUnitPrice: 100,
    reorderLevel: 5,
    quantityOnHand: 10,
    isLowStock: false,
    isProductActive: true,
    isSupplierActive: true,
    supplierId: id,
    supplierCode: name,
    supplierName: name,
    isPreferred: false,
    requestNumber: name,
    requesterName: name,
    requiredDate: '2030-12-31',
    estimatedTotal: 100,
    purchaseRequestId: id,
    purchaseRequestNumber: name,
    quotationId: id,
    quotationNumber: name,
    quotationDate: '2026-10-01',
    validUntil: '2030-12-31',
    purchaseOrderId: id,
    purchaseOrderNumber: name,
    orderDate: '2026-10-01',
    expectedDeliveryDate: '2030-12-31',
    goodsReceiptNumber: name,
    receivedDate: '2026-10-01',
    totalAmount: 100,
    status: 'Draft',
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-10-01T08:00:00Z',
    lastUpdatedAtUtc: '2026-10-01T08:00:00Z',
    sentAtUtc: null,
    sourceReference: name,
    sourceType: 'PurchaseOrder',
    recipientEmail: `${id}@example.test`,
    subject: name,
    attemptCount: 0,
    entityType: 'PurchaseRequest',
    version: 1,
    isPublished: false,
    publishedAtUtc: null,
    stepCount: 2,
    items: [],
    workflow: {
      currentStepCode: 'DRAFT',
      currentStepName: 'Draft',
      status: 'Running',
      availableActions: [],
      history: [],
    },
  }
}

const fetchMock = vi.fn<typeof fetch>()

beforeEach(() => {
  fetchMock.mockReset()
  fetchMock.mockImplementation(async (input) => {
    const url = String(input)
    const data = url.endsWith('/roles')
      ? ['ADMIN']
      : Array.from({ length: 21 }, (_, i) => record(i + 1))
    return new Response(JSON.stringify(data), { status: 200 })
  })
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('listing pagination', () => {
  it.each(cases)('paginates and searches %s without refetching', async (viewPath, itemLabel) => {
    const pinia = createPinia()
    useAuthStore(pinia).$patch({
      session: {
        accessToken: 'test-token',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        user: {
          id: 4,
          email: 'admin@example.test',
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
      routes: [{ path: '/', name: 'purchase-requests', component: { template: '<div />' } }],
    })
    await router.push('/')
    await router.isReady()
    const view = await views[`../${viewPath}.vue`]!()
    const wrapper = mount(view.default, { global: { plugins: [pinia, router] } })
    try {
      await flushPromises()
      const requestsAtLoad = fetchMock.mock.calls.length
      const pagination = wrapper.get('.table-pagination')
      const previous = pagination.get('button:first-child')
      const next = pagination.get('button:last-child')
      const pageSize = pagination.get('select')

      expect(wrapper.findAll('tbody tr')).toHaveLength(10)
      expect(pagination.get('.pagination-summary').text()).toBe(`Showing 1–10 of 21 ${itemLabel}`)
      expect(previous.attributes('disabled')).toBeDefined()
      await next.trigger('click')
      expect(wrapper.get('tbody tr').text()).toContain('Record 0011')
      await next.trigger('click')
      expect(wrapper.findAll('tbody tr')).toHaveLength(1)
      expect(wrapper.get('tbody tr').text()).toContain('Record 0021')
      expect(next.attributes('disabled')).toBeDefined()

      await previous.trigger('click')
      await wrapper.get('.search-control input').setValue('Record 0001')
      expect(wrapper.findAll('tbody tr')).toHaveLength(1)
      expect(pagination.text()).toContain('Page 1 of 1')
      expect(pagination.get('.pagination-summary').text()).toBe(`Showing 1–1 of 1 ${itemLabel}`)
      await wrapper.get('.search-control input').setValue('')
      expect(wrapper.findAll('tbody tr')).toHaveLength(10)

      await next.trigger('click')
      await pageSize.setValue('20')
      expect(wrapper.findAll('tbody tr')).toHaveLength(20)
      expect(pagination.text()).toContain('Page 1 of 2')
      await next.trigger('click')
      expect(wrapper.findAll('tbody tr')).toHaveLength(1)
      await pageSize.setValue('50')
      expect(wrapper.findAll('tbody tr')).toHaveLength(21)
      expect(pagination.text()).toContain('Page 1 of 1')

      await wrapper.get('.search-control input').setValue('no matching record')
      expect(wrapper.find('tbody').exists()).toBe(false)
      expect(wrapper.find('.table-pagination').exists()).toBe(false)
      expect(fetchMock).toHaveBeenCalledTimes(requestsAtLoad)
    } finally {
      wrapper.unmount()
    }
  })
})
