import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { PurchaseOrder } from '@/types/purchaseOrder'
import PurchaseOrderCancelDialog from '../PurchaseOrderCancelDialog.vue'

const order = {
  id: 5,
  purchaseOrderNumber: 'PO-0005',
  status: 'Issued',
} as PurchaseOrder

describe('PurchaseOrderCancelDialog', () => {
  it('requires a reason before confirming cancellation', async () => {
    const wrapper = mount(PurchaseOrderCancelDialog, {
      props: { purchaseOrder: order, cancelling: false, errorMessage: '' },
    })

    await wrapper.get('form').trigger('submit')

    expect(wrapper.text()).toContain('Cancellation reason is required.')
    expect(wrapper.emitted('confirm')).toBeUndefined()
  })

  it('trims and emits the cancellation reason', async () => {
    const wrapper = mount(PurchaseOrderCancelDialog, {
      props: { purchaseOrder: order, cancelling: false, errorMessage: '' },
    })

    await wrapper.get('textarea').setValue(' Supplier unavailable ')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('confirm')?.[0]).toEqual(['Supplier unavailable'])
  })
})
