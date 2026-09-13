import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import type { PurchaseRequest } from '@/types/purchaseRequest'
import type {
  CreateQuotationRequest,
  Quotation,
  QuotationComparison,
  UpdateQuotationRequest,
} from '@/types/quotation'
import type { Supplier } from '@/types/supplier'
import type { SupplierProduct, SupplierProductFilters } from '@/types/supplierProduct'
import QuotationListView from '../QuotationListView.vue'

const mocks = vi.hoisted(() => ({
  getQuotations: vi.fn<() => Promise<Quotation[]>>(),
  getComparison: vi.fn<(purchaseRequestId: number) => Promise<QuotationComparison>>(),
  create: vi.fn<(payload: CreateQuotationRequest) => Promise<Quotation>>(),
  update: vi.fn<(id: number, payload: UpdateQuotationRequest) => Promise<Quotation>>(),
  submit: vi.fn<(id: number) => Promise<Quotation>>(),
  select: vi.fn<(id: number) => Promise<Quotation>>(),
  delete: vi.fn<(id: number) => Promise<void>>(),
  getPurchaseRequests: vi.fn<(stepCode?: string) => Promise<PurchaseRequest[]>>(),
  getSuppliers: vi.fn<(includeInactive?: boolean) => Promise<Supplier[]>>(),
  getSupplierProducts: vi.fn<(filters?: SupplierProductFilters) => Promise<SupplierProduct[]>>(),
}))

vi.mock('@/services/quotationService', () => ({
  quotationService: {
    getAll: mocks.getQuotations,
    getComparison: mocks.getComparison,
    create: mocks.create,
    update: mocks.update,
    submit: mocks.submit,
    select: mocks.select,
    delete: mocks.delete,
  },
}))

vi.mock('@/services/purchaseRequestService', () => ({
  purchaseRequestService: { getAll: mocks.getPurchaseRequests },
}))

vi.mock('@/services/supplierService', () => ({
  supplierService: { getAll: mocks.getSuppliers },
}))

vi.mock('@/services/supplierProductService', () => ({
  supplierProductService: { getAll: mocks.getSupplierProducts },
}))

const purchaseRequest: PurchaseRequest = {
  id: 7,
  requestNumber: 'PR-0007',
  requesterName: 'Demo Requester',
  departmentId: 1,
  departmentCode: 'IT',
  departmentName: 'Information Technology',
  requiredDate: '2026-10-01',
  justification: 'New workstation',
  estimatedTotal: 2400,
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

const supplier: Supplier = {
  id: 1,
  code: 'SUP-0001',
  name: 'Example Supplies',
  contactPerson: null,
  email: null,
  phone: null,
  address: null,
  isActive: true,
  createdAtUtc: '2026-09-01T00:00:00Z',
  updatedAtUtc: null,
}

const supplierProduct: SupplierProduct = {
  id: 1,
  supplierId: 1,
  supplierCode: 'SUP-0001',
  supplierName: 'Example Supplies',
  productId: 1,
  productCode: 'ITEM-0001',
  productName: 'Monitor',
  unitOfMeasureCode: 'UNIT',
  unitOfMeasureName: 'Unit',
  productDefaultUnitPrice: 1200,
  isPreferred: true,
  isActive: true,
  createdAtUtc: '2026-09-01T00:00:00Z',
  updatedAtUtc: null,
}

function quotation(status: Quotation['status'] = 'Draft'): Quotation {
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
    notes: null,
    status,
    totalAmount: 2200,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    createdAtUtc: '2026-09-13T00:00:00Z',
    updatedAtUtc: null,
    submittedAtUtc: status === 'Draft' ? null : '2026-09-13T01:00:00Z',
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
  }
}

function comparison(): QuotationComparison {
  const submitted = quotation('Submitted')
  return {
    purchaseRequestId: 7,
    purchaseRequestNumber: 'PR-0007',
    selectedQuotationId: null,
    lowestTotalAmount: 2200,
    quotations: [
      {
        quotationId: submitted.id,
        quotationNumber: submitted.quotationNumber,
        supplierId: submitted.supplierId,
        supplierCode: submitted.supplierCode,
        supplierName: submitted.supplierName,
        supplierQuotationReference: submitted.supplierQuotationReference,
        quotationDate: submitted.quotationDate,
        validUntil: submitted.validUntil,
        status: submitted.status,
        totalAmount: submitted.totalAmount,
        isLowestTotal: true,
        isSelected: false,
        items: submitted.items,
      },
    ],
  }
}

describe('QuotationListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getQuotations.mockResolvedValue([quotation()])
    mocks.getPurchaseRequests.mockResolvedValue([purchaseRequest])
    mocks.getSuppliers.mockResolvedValue([supplier])
    mocks.getSupplierProducts.mockResolvedValue([supplierProduct])
    mocks.submit.mockResolvedValue(quotation('Submitted'))
    mocks.select.mockResolvedValue(quotation('Selected'))
    mocks.delete.mockResolvedValue(undefined)
    mocks.getComparison.mockResolvedValue(comparison())
    vi.stubGlobal(
      'confirm',
      vi.fn(() => true),
    )
  })

  it('loads approved requests and displays quotation actions by status', async () => {
    const wrapper = mount(QuotationListView)
    await flushPromises()

    expect(mocks.getPurchaseRequests).toHaveBeenCalledWith('APPROVED')
    expect(wrapper.get('h1').text()).toBe('Supplier Quotations')
    expect(wrapper.get('tbody').text()).toContain('QT-0003')
    expect(wrapper.get('tbody').text()).toContain('Edit')
    expect(wrapper.get('tbody').text()).toContain('Submit')
    expect(wrapper.get('tbody').text()).not.toContain('Compare')
  })

  it('submits a draft, shows a toast, and reloads the register', async () => {
    const wrapper = mount(QuotationListView)
    await flushPromises()

    const submitButton = wrapper.findAll('button').find((button) => button.text() === 'Submit')
    await submitButton?.trigger('click')
    await flushPromises()

    expect(window.confirm).toHaveBeenCalled()
    expect(mocks.submit).toHaveBeenCalledWith(3)
    expect(mocks.getQuotations).toHaveBeenCalledTimes(2)
    expect(wrapper.get('.success-toast').text()).toContain('QT-0003 was submitted')
  })

  it('opens comparison and selects the winning quotation', async () => {
    mocks.getQuotations.mockResolvedValue([quotation('Submitted')])
    const wrapper = mount(QuotationListView)
    await flushPromises()

    const compareButton = wrapper.findAll('button').find((button) => button.text() === 'Compare')
    await compareButton?.trigger('click')
    await flushPromises()

    expect(mocks.getComparison).toHaveBeenCalledWith(7)
    expect(wrapper.get('[role="dialog"]').text()).toContain('Lowest')

    const selectButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Select winner')
    await selectButton?.trigger('click')
    await flushPromises()

    expect(mocks.select).toHaveBeenCalledWith(3)
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(wrapper.get('.success-toast').text()).toContain('winning quotation')
  })
})
