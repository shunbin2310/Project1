import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { GoodsReceipt } from '@/types/goodsReceipt'
import GoodsReceiptDetails from '../GoodsReceiptDetails.vue'

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
    notes: 'Boxes checked.',
    status: 'Posted',
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-16T00:00:00Z',
    updatedAtUtc: null,
    postedAtUtc: '2026-09-16T01:00:00Z',
    postedByUserId: 4,
    postedByName: 'Demo Admin',
    items: [
      {
        id: 1,
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

describe('GoodsReceiptDetails', () => {
  it('shows the source order, supplier delivery and posted audit information', () => {
    const wrapper = mount(GoodsReceiptDetails, { props: { goodsReceipt: receipt() } })

    expect(wrapper.get('#goods-receipt-details-title').text()).toBe('GR-0009')
    expect(wrapper.get('.goods-receipt-status').text()).toBe('Posted')
    expect(wrapper.text()).toContain('PO-0005')
    expect(wrapper.text()).toContain('DN-009')
    expect(wrapper.text()).toContain('Office Chair')
    expect(wrapper.get('.goods-receipt-audit').text()).toContain('Posted')
  })

  it('emits close from both close buttons', async () => {
    const wrapper = mount(GoodsReceiptDetails, { props: { goodsReceipt: receipt() } })

    await wrapper.get('[aria-label="Close details"]').trigger('click')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Close')
      ?.trigger('click')

    expect(wrapper.emitted('close')).toHaveLength(2)
  })
})
