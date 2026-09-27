import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { PurchaseOrder } from '@/types/purchaseOrder'
import type { Quotation } from '@/types/quotation'
import PurchaseOrderForm from '../PurchaseOrderForm.vue'

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
    validUntil: '2026-10-15',
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

function purchaseOrder(): PurchaseOrder {
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
    notes: null,
    status: 'Draft',
    totalAmount: 1500,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
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

describe('PurchaseOrderForm', () => {
  it('previews the selected quotation and emits a draft payload', async () => {
    const wrapper = mount(PurchaseOrderForm, {
      props: {
        purchaseOrder: null,
        quotations: [quotation()],
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.text()).toContain('QT-0008')
    expect(wrapper.text()).toContain('Office Chair')
    expect(wrapper.text().replace(/\s/g, '')).toContain('RM1,500.00')

    await wrapper.get('#purchase-order-delivery-date').setValue('2026-09-30')
    await wrapper.get('#purchase-order-address').setValue(' Main warehouse ')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({
      quotationId: 8,
      expectedDeliveryDate: '2026-09-30',
      deliveryAddress: 'Main warehouse',
    })
  })

  it('prevents an expected delivery date before the order date', async () => {
    const wrapper = mount(PurchaseOrderForm, {
      props: {
        purchaseOrder: null,
        quotations: [quotation()],
        saving: false,
        errorMessage: '',
      },
    })

    await wrapper.get('#purchase-order-date').setValue('2026-10-01')
    await wrapper.get('#purchase-order-delivery-date').setValue('2026-09-30')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.text()).toContain('Expected delivery must be on or after the order date.')
    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('keeps the source quotation read-only when editing', () => {
    const wrapper = mount(PurchaseOrderForm, {
      props: {
        purchaseOrder: purchaseOrder(),
        quotations: [quotation()],
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.get('#purchase-order-form-title').text()).toBe('Edit PO-0005')
    expect(wrapper.get('#purchase-order-quotation').attributes('readonly')).toBeDefined()
    expect((wrapper.get('#purchase-order-address').element as HTMLTextAreaElement).value).toBe(
      'Main warehouse',
    )
  })
})
