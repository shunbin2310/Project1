import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import type {
  InventoryBalance,
  InventoryBalanceFilters,
  InventoryTransaction,
  InventoryTransactionFilters,
} from '@/types/inventory'
import InventoryListView from '../InventoryListView.vue'

const mocks = vi.hoisted(() => ({
  getAll: vi.fn<(filters?: InventoryBalanceFilters) => Promise<InventoryBalance[]>>(),
  getTransactions:
    vi.fn<
      (productId: number, filters?: InventoryTransactionFilters) => Promise<InventoryTransaction[]>
    >(),
}))

vi.mock('@/services/inventoryService', () => ({
  inventoryService: {
    getAll: mocks.getAll,
    getTransactions: mocks.getTransactions,
  },
}))

function balance(
  productId: number,
  productName: string,
  quantityOnHand: number,
  reorderLevel: number,
  overrides: Partial<InventoryBalance> = {},
): InventoryBalance {
  return {
    productId,
    productCode: `ITEM-${String(productId).padStart(4, '0')}`,
    productName,
    quantityOnHand,
    reorderLevel,
    isLowStock: quantityOnHand <= reorderLevel,
    isProductActive: true,
    lastUpdatedAtUtc: '2026-09-20T10:30:00Z',
    ...overrides,
  }
}

const transaction: InventoryTransaction = {
  id: 11,
  productId: 3,
  productCode: 'ITEM-0003',
  productName: 'Office Chair',
  type: 'GoodsReceipt',
  quantityChange: 4,
  quantityBefore: 0,
  quantityAfter: 4,
  referenceType: 'GoodsReceipt',
  referenceId: 9,
  referenceNumber: 'GR-0009',
  goodsReceiptItemId: 14,
  performedByUserId: 4,
  performedByName: 'Demo Admin',
  occurredAtUtc: '2026-09-20T10:30:00Z',
}

describe('InventoryListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAll.mockResolvedValue([
      balance(3, 'Office Chair', 4, 2),
      balance(4, 'Printer Paper', 2, 5),
      balance(5, 'USB Cable', 0, 3),
      balance(6, 'Old Monitor', 10, 2, { isProductActive: false }),
    ])
    mocks.getTransactions.mockResolvedValue([transaction])
  })

  it('loads balances and shows active stock summary', async () => {
    const wrapper = mount(InventoryListView)
    await flushPromises()

    expect(mocks.getAll).toHaveBeenCalledWith({ includeInactive: true })
    expect(wrapper.get('h1').text()).toBe('Inventory')
    const cards = wrapper.findAll('.summary-card')
    expect(cards[0]?.text()).toContain('3')
    expect(cards[1]?.text()).toContain('1')
    expect(cards[2]?.text()).toContain('1')
    expect(wrapper.get('tbody').text()).toContain('Office Chair')
    expect(wrapper.get('tbody').text()).not.toContain('Old Monitor')
  })

  it('filters products by stock status and can reveal inactive products', async () => {
    const wrapper = mount(InventoryListView)
    await flushPromises()

    await wrapper.get('select[aria-label="Filter by stock status"]').setValue('OutOfStock')
    expect(wrapper.get('tbody').text()).toContain('USB Cable')
    expect(wrapper.get('tbody').text()).not.toContain('Office Chair')

    await wrapper.get('select[aria-label="Filter by stock status"]').setValue('')
    await wrapper.get('input[type="checkbox"]').setValue(true)
    expect(wrapper.get('tbody').text()).toContain('Old Monitor')
    expect(wrapper.get('tbody').text()).toContain('Inactive')
  })

  it('opens the product ledger and loads its transactions', async () => {
    const wrapper = mount(InventoryListView)
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'View history')
      ?.trigger('click')
    await flushPromises()

    expect(mocks.getTransactions).toHaveBeenCalledWith(3, {
      type: undefined,
      dateFrom: undefined,
      dateTo: undefined,
    })
    expect(wrapper.get('[role="dialog"]').text()).toContain('Office Chair')
    expect(wrapper.get('[role="dialog"]').text()).toContain('GR-0009')
    expect(wrapper.get('[role="dialog"]').text()).toContain('+4')
  })

  it('paginates stock movements locally and resets after applying ledger filters', async () => {
    const transactions = Array.from({ length: 21 }, (_, index) => ({
      ...transaction,
      id: index + 1,
      referenceNumber: `GR-${String(index + 1).padStart(4, '0')}`,
    }))
    mocks.getTransactions.mockResolvedValue(transactions)
    const wrapper = mount(InventoryListView)
    try {
      await flushPromises()
      await wrapper.get('tbody button').trigger('click')
      await flushPromises()
      const dialog = wrapper.get('[role="dialog"]')
      const pagination = dialog.get('.table-pagination')
      const next = pagination.get('button:last-child')

      expect(dialog.findAll('tbody tr')).toHaveLength(10)
      expect(pagination.text()).toContain('Showing 1–10 of 21 transactions')
      await next.trigger('click')
      expect(dialog.get('tbody tr').text()).toContain('GR-0011')
      await next.trigger('click')
      expect(dialog.findAll('tbody tr')).toHaveLength(1)
      expect(next.attributes('disabled')).toBeDefined()
      expect(mocks.getTransactions).toHaveBeenCalledTimes(1)

      await dialog.get('.inventory-history-filters select').setValue('GoodsReceipt')
      await dialog.get('form').trigger('submit')
      await flushPromises()
      expect(mocks.getTransactions).toHaveBeenLastCalledWith(3, {
        type: 'GoodsReceipt',
        dateFrom: undefined,
        dateTo: undefined,
      })
      expect(dialog.get('.table-pagination').text()).toContain('Page 1 of 3')
      expect(dialog.get('tbody tr').text()).toContain('GR-0001')
      await dialog.get('.table-pagination select').setValue('20')
      expect(dialog.findAll('tbody tr')).toHaveLength(20)
      expect(mocks.getTransactions).toHaveBeenCalledTimes(2)

      mocks.getTransactions.mockResolvedValue([])
      await dialog.get('form').trigger('submit')
      await flushPromises()
      expect(dialog.find('.table-pagination').exists()).toBe(false)
      expect(dialog.text()).toContain('No stock movements found')
    } finally {
      wrapper.unmount()
    }
  })
})
