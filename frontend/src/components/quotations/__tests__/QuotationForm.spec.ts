import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { PurchaseRequest } from '@/types/purchaseRequest'
import type { Quotation } from '@/types/quotation'
import type { Supplier } from '@/types/supplier'
import type { SupplierProduct } from '@/types/supplierProduct'
import QuotationForm from '../QuotationForm.vue'

const purchaseRequest: PurchaseRequest = {
  id: 7,
  requestNumber: 'PR-0007',
  requesterName: 'Demo Requester',
  departmentId: 1,
  departmentCode: 'IT',
  departmentName: 'Information Technology',
  requiredDate: '2026-10-01',
  justification: 'New workstations',
  estimatedTotal: 2600,
  createdAtUtc: '2026-09-01T00:00:00Z',
  updatedAtUtc: null,
  items: [
    {
      id: 11,
      productId: 1,
      productCode: 'ITEM-0001',
      productName: 'Monitor',
      unitOfMeasureCode: 'UNIT',
      quantity: 2,
      estimatedUnitPrice: 1200,
      lineTotal: 2400,
    },
    {
      id: 12,
      productId: 2,
      productCode: 'ITEM-0002',
      productName: 'Keyboard',
      unitOfMeasureCode: 'UNIT',
      quantity: 2,
      estimatedUnitPrice: 100,
      lineTotal: 200,
    },
  ],
  workflow: {
    id: 7,
    templateCode: 'PURCHASE_REQUEST',
    templateName: 'Purchase Request Approval',
    templateVersion: 1,
    entityType: 'PurchaseRequest',
    entityId: 7,
    status: 'Completed',
    currentStepCode: 'APPROVED',
    currentStepName: 'Approved',
    startedAtUtc: '2026-09-01T00:00:00Z',
    completedAtUtc: '2026-09-02T00:00:00Z',
    availableActions: [],
    history: [],
  },
}

const suppliers: Supplier[] = [
  {
    id: 1,
    code: 'SUP-0001',
    name: 'Complete Supplier',
    contactPerson: null,
    email: null,
    phone: null,
    address: null,
    isActive: true,
    createdAtUtc: '2026-09-01T00:00:00Z',
    updatedAtUtc: null,
  },
  {
    id: 2,
    code: 'SUP-0002',
    name: 'Partial Supplier',
    contactPerson: null,
    email: null,
    phone: null,
    address: null,
    isActive: true,
    createdAtUtc: '2026-09-01T00:00:00Z',
    updatedAtUtc: null,
  },
]

const supplierProducts: SupplierProduct[] = [
  relationship(1, 1, 'ITEM-0001'),
  relationship(1, 2, 'ITEM-0002'),
  relationship(2, 1, 'ITEM-0001'),
]

function relationship(supplierId: number, productId: number, productCode: string): SupplierProduct {
  return {
    id: supplierId * 10 + productId,
    supplierId,
    supplierCode: `SUP-000${supplierId}`,
    supplierName: supplierId === 1 ? 'Complete Supplier' : 'Partial Supplier',
    productId,
    productCode,
    productName: productCode,
    unitOfMeasureCode: 'UNIT',
    unitOfMeasureName: 'Unit',
    productDefaultUnitPrice: 100,
    isPreferred: false,
    isActive: true,
    createdAtUtc: '2026-09-01T00:00:00Z',
    updatedAtUtc: null,
  }
}

function quotation(): Quotation {
  return {
    id: 4,
    quotationNumber: 'QT-0004',
    purchaseRequestId: 7,
    purchaseRequestNumber: 'PR-0007',
    supplierId: 1,
    supplierCode: 'SUP-0001',
    supplierName: 'Complete Supplier',
    supplierQuotationReference: null,
    quotationDate: '2026-09-13',
    validUntil: null,
    notes: null,
    status: 'Draft',
    totalAmount: 2300,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-13T00:00:00Z',
    updatedAtUtc: null,
    submittedAtUtc: null,
    selectedAtUtc: null,
    items: purchaseRequest.items.map((item, index) => ({
      id: 20 + index,
      purchaseRequestItemId: item.id,
      productId: item.productId,
      productCode: item.productCode,
      productName: item.productName,
      unitOfMeasureCode: item.unitOfMeasureCode,
      quantity: item.quantity,
      unitPrice: index === 0 ? 1100 : 50,
      lineTotal: index === 0 ? 2200 : 100,
    })),
  }
}

function mountForm(
  editingQuotation: Quotation | null = null,
  existingQuotations: Quotation[] = [],
) {
  return mount(QuotationForm, {
    props: {
      quotation: editingQuotation,
      purchaseRequests: [purchaseRequest],
      suppliers,
      supplierProducts,
      existingQuotations,
      saving: false,
      errorMessage: '',
    },
  })
}

describe('QuotationForm', () => {
  it('only lists suppliers that can provide every request item', () => {
    const wrapper = mountForm()

    expect(wrapper.get('#quotation-supplier').text()).toContain('Complete Supplier')
    expect(wrapper.get('#quotation-supplier').text()).not.toContain('Partial Supplier')
  })

  it('allows zero prices in a draft and emits every purchase request item', async () => {
    const wrapper = mountForm()
    await wrapper.get('#quotation-supplier').setValue('1')
    await wrapper.get('form').trigger('submit')

    const emitted = wrapper.emitted('save')?.[0]
    expect(emitted?.[1]).toBe(false)
    expect(emitted?.[0]).toMatchObject({
      purchaseRequestId: 7,
      supplierId: 1,
      items: [
        { purchaseRequestItemId: 11, unitPrice: 0 },
        { purchaseRequestItemId: 12, unitPrice: 0 },
      ],
    })
  })

  it('requires a positive price for every item before submission', async () => {
    const wrapper = mountForm()
    await wrapper.get('#quotation-supplier').setValue('1')
    const saveAndSubmit = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Save and submit'))
    await saveAndSubmit?.trigger('click')

    expect(wrapper.text()).toContain('Enter a unit price greater than zero')
    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('keeps purchase request and supplier read-only while editing', () => {
    const wrapper = mountForm(quotation())

    expect(wrapper.get('#quotation-request').attributes('readonly')).toBeDefined()
    expect(wrapper.get('#quotation-supplier').attributes('readonly')).toBeDefined()
    expect(wrapper.text().replace(/\s/g, '')).toContain('RM2,300.00')
  })
})
