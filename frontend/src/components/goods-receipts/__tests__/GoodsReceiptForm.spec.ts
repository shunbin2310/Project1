import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { GoodsReceipt, ReceivablePurchaseOrder } from '@/types/goodsReceipt'
import GoodsReceiptForm from '../GoodsReceiptForm.vue'

function purchaseOrder(): ReceivablePurchaseOrder {
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
    status: 'PartiallyReceived',
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
        quantity: 10,
        unitPrice: 150,
        lineTotal: 1500,
        previouslyReceivedQuantity: 4,
        remainingQuantity: 6,
      },
    ],
  }
}

function receipt(): GoodsReceipt {
  return {
    id: 9,
    goodsReceiptNumber: 'GR-0009',
    purchaseOrderId: 5,
    purchaseOrderNumber: 'PO-0005',
    supplierId: 2,
    supplierCode: 'SUP-0002',
    supplierName: 'Office Supply Co',
    supplierDeliveryNoteNumber: 'DN-009',
    receivedDate: '2026-09-16',
    notes: null,
    status: 'Draft',
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-16T00:00:00Z',
    updatedAtUtc: null,
    postedAtUtc: null,
    postedByUserId: null,
    postedByName: null,
    items: [
      {
        id: 1,
        purchaseOrderItemId: 2,
        productId: 3,
        productCode: 'ITEM-0003',
        productName: 'Office Chair',
        unitOfMeasureCode: 'UNIT',
        orderedQuantity: 10,
        quantityReceived: 2,
      },
    ],
  }
}

describe('GoodsReceiptForm', () => {
  it('shows receiving progress and emits only a positive received quantity', async () => {
    const wrapper = mount(GoodsReceiptForm, {
      props: {
        goodsReceipt: null,
        purchaseOrders: [purchaseOrder()],
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.text()).toContain('Previously received')
    expect(wrapper.text()).toContain('Office Chair')
    await wrapper.get('.table-quantity-input').setValue('3')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({
      purchaseOrderId: 5,
      items: [{ purchaseOrderItemId: 2, quantityReceived: 3 }],
    })
  })

  it('prevents receiving more than the remaining quantity', async () => {
    const wrapper = mount(GoodsReceiptForm, {
      props: {
        goodsReceipt: null,
        purchaseOrders: [purchaseOrder()],
        saving: false,
        errorMessage: '',
      },
    })

    await wrapper.get('.table-quantity-input').setValue('7')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.text()).toContain(
      'Received quantity cannot exceed the remaining purchase order quantity.',
    )
    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('keeps the purchase order read-only when editing a draft', () => {
    const wrapper = mount(GoodsReceiptForm, {
      props: {
        goodsReceipt: receipt(),
        purchaseOrders: [purchaseOrder()],
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.get('#goods-receipt-form-title').text()).toBe('Edit GR-0009')
    expect(wrapper.get('#goods-receipt-order').attributes('readonly')).toBeDefined()
    expect((wrapper.get('.table-quantity-input').element as HTMLInputElement).value).toBe('2')
  })
})
