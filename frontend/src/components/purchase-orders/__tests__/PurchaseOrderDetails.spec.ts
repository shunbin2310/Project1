import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { PurchaseOrder } from '@/types/purchaseOrder'
import PurchaseOrderDetails from '../PurchaseOrderDetails.vue'

const actor = { id: 4, name: 'Demo Admin', roles: ['ADMIN'] }

function purchaseOrder(overrides: Partial<PurchaseOrder> = {}): PurchaseOrder {
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
    expectedDeliveryDate: '2026-09-30',
    deliveryAddress: 'Main warehouse',
    notes: 'Deliver during office hours.',
    status: 'Issued',
    totalAmount: 1500,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-16T00:00:00Z',
    updatedAtUtc: null,
    issuedAtUtc: '2026-09-16T01:00:00Z',
    issuedByUserId: 4,
    issuedByName: 'Demo Admin',
    cancelledAtUtc: null,
    cancelledByUserId: null,
    cancelledByName: null,
    cancellationReason: null,
    emailDelivery: null,
    workflow: null,
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
    ...overrides,
  }
}

describe('PurchaseOrderDetails', () => {
  it('shows order source, item snapshot, and issue audit information', () => {
    const wrapper = mount(PurchaseOrderDetails, {
      props: { purchaseOrder: purchaseOrder(), actor },
    })

    expect(wrapper.get('#purchase-order-details-title').text()).toBe('PO-0005')
    expect(wrapper.get('.purchase-order-status').text()).toBe('Issued')
    expect(wrapper.text()).toContain('PR-0004')
    expect(wrapper.text()).toContain('QT-0008')
    expect(wrapper.text()).toContain('Office Chair')
    expect(wrapper.text().replace(/\s/g, '')).toContain('RM1,500.00')
    expect(wrapper.get('.purchase-order-audit').text()).toContain('Issued')
  })

  it('shows a cancellation reason for cancelled orders', () => {
    const wrapper = mount(PurchaseOrderDetails, {
      props: {
        purchaseOrder: purchaseOrder({
          status: 'Cancelled',
          cancelledAtUtc: '2026-09-17T01:00:00Z',
          cancelledByUserId: 4,
          cancelledByName: 'Demo Admin',
          cancellationReason: 'Supplier could not meet the delivery date.',
        }),
        actor,
      },
    })

    expect(wrapper.get('.purchase-order-cancellation').text()).toContain(
      'Supplier could not meet the delivery date.',
    )
  })

  it('formats the partially received status label', () => {
    const wrapper = mount(PurchaseOrderDetails, {
      props: { purchaseOrder: purchaseOrder({ status: 'PartiallyReceived' }), actor },
    })

    expect(wrapper.get('.purchase-order-status').text()).toBe('Partially received')
  })

  it('shows an authorized approval action and emits it', async () => {
    const approveAction = {
      code: 'APPROVE',
      name: 'Approve purchase order',
      requiresComment: false,
      toStepCode: 'APPROVED',
      toStepName: 'Approved',
      actioners: [{ actionerType: 'Role' as const, actionerKey: 'PURCHASE_ORDER_APPROVER' }],
    }
    const wrapper = mount(PurchaseOrderDetails, {
      props: {
        purchaseOrder: purchaseOrder({
          status: 'PendingApproval',
          workflow: {
            id: 10,
            templateCode: 'PURCHASE_ORDER',
            templateName: 'Purchase Order Approval',
            templateVersion: 1,
            entityType: 'PurchaseOrder',
            entityId: 5,
            status: 'Running',
            currentStepCode: 'PENDING_APPROVAL',
            currentStepName: 'Pending Approval',
            startedAtUtc: '2026-09-16T00:00:00Z',
            completedAtUtc: null,
            availableActions: [approveAction],
            history: [],
          },
        }),
        actor: {
          id: 8,
          name: 'Purchase Order Approver',
          roles: ['PURCHASE_ORDER_APPROVER'],
        },
      },
    })

    const button = wrapper.findAll('button').find((item) => item.text() === approveAction.name)
    expect(button?.attributes('disabled')).toBeUndefined()
    await button?.trigger('click')
    expect(wrapper.emitted('action')?.[0]?.[0]).toEqual(approveAction)
  })

  it('emits close from both close buttons', async () => {
    const wrapper = mount(PurchaseOrderDetails, {
      props: { purchaseOrder: purchaseOrder(), actor },
    })

    await wrapper.get('[aria-label="Close details"]').trigger('click')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Close')
      ?.trigger('click')

    expect(wrapper.emitted('close')).toHaveLength(2)
  })
})
