import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { Quotation } from '@/types/quotation'
import QuotationDetails from '../QuotationDetails.vue'

function quotation(overrides: Partial<Quotation> = {}): Quotation {
  return {
    id: 3,
    quotationNumber: 'QT-0003',
    purchaseRequestId: 7,
    purchaseRequestNumber: 'PR-0007',
    supplierId: 1,
    supplierCode: 'SUP-0001',
    supplierName: 'Example Supplies',
    supplierQuotationReference: 'SUP-Q-003',
    quotationDate: '2026-09-13',
    validUntil: '2026-10-13',
    notes: 'Delivery within five working days.',
    status: 'Submitted',
    totalAmount: 2200,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-13T05:00:00Z',
    updatedAtUtc: null,
    submittedAtUtc: '2026-09-13T06:00:00Z',
    selectedAtUtc: null,
    items: [
      {
        id: 4,
        purchaseRequestItemId: 11,
        productId: 1,
        productCode: 'ITEM-0001',
        productName: 'Monitor',
        unitOfMeasureCode: 'UNIT',
        quantity: 2,
        unitPrice: 1100,
        lineTotal: 2200,
      },
    ],
    ...overrides,
  }
}

describe('QuotationDetails', () => {
  it('shows the quotation status and prioritised supplier and request summary', () => {
    const wrapper = mount(QuotationDetails, { props: { quotation: quotation() } })

    expect(wrapper.get('#quotation-details-title').text()).toBe('QT-0003')
    expect(wrapper.get('.quotation-status').text()).toBe('Submitted')
    expect(wrapper.get('.quotation-summary-supplier').text()).toBe('Example Supplies')
    expect(wrapper.get('.quotation-summary-request').text()).toContain('PR-0007')
    expect(wrapper.text()).toContain('SUP-Q-003')
    expect(wrapper.text().replace(/\s/g, '')).toContain('RM2,200.00')
  })

  it('shows clear fallback text for optional supplier information', () => {
    const wrapper = mount(QuotationDetails, {
      props: {
        quotation: quotation({
          supplierQuotationReference: null,
          validUntil: null,
          notes: null,
        }),
      },
    })

    expect(wrapper.text()).toContain('Not provided')
    expect(wrapper.text()).toContain('Not set')
    expect(wrapper.text()).toContain('No notes provided.')
  })

  it('closes from the header and footer buttons', async () => {
    const wrapper = mount(QuotationDetails, { props: { quotation: quotation() } })

    await wrapper.get('[aria-label="Close details"]').trigger('click')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Close')
      ?.trigger('click')

    expect(wrapper.emitted('close')).toHaveLength(2)
  })
})
