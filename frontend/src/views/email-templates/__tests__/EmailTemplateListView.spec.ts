import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import EmailTemplateEditor from '@/components/email-templates/EmailTemplateEditor.vue'
import EmailTemplatePreview from '@/components/email-templates/EmailTemplatePreview.vue'
import type {
  EmailTemplate,
  EmailTemplatePreview as EmailTemplatePreviewModel,
  EmailTemplateSummary,
  PreviewEmailTemplateRequest,
  UpdateEmailTemplateRequest,
} from '@/types/emailTemplate'
import EmailTemplateListView from '../EmailTemplateListView.vue'

const mocks = vi.hoisted(() => ({
  getAll: vi.fn<() => Promise<EmailTemplateSummary[]>>(),
  getById: vi.fn<(id: number) => Promise<EmailTemplate>>(),
  createVersion: vi.fn<(id: number) => Promise<EmailTemplate>>(),
  update: vi.fn<(id: number, payload: UpdateEmailTemplateRequest) => Promise<EmailTemplate>>(),
  preview: vi.fn<(payload: PreviewEmailTemplateRequest) => Promise<EmailTemplatePreviewModel>>(),
  publish: vi.fn<(id: number) => Promise<EmailTemplate>>(),
  delete: vi.fn<(id: number) => Promise<void>>(),
}))

vi.mock('@/services/emailTemplateService', () => ({
  ApiError: Error,
  emailTemplateService: mocks,
}))

const summaries: EmailTemplateSummary[] = [
  {
    id: 2,
    code: 'PURCHASE_ORDER_ISSUED',
    name: 'Purchase Order Issued Email',
    version: 2,
    status: 'Draft',
    toRule: 'SUPPLIER_EMAIL',
    createdAtUtc: '2026-09-29T00:00:00Z',
    publishedAtUtc: null,
    updatedAtUtc: null,
  },
  {
    id: 1,
    code: 'PURCHASE_ORDER_ISSUED',
    name: 'Purchase Order Issued Email',
    version: 1,
    status: 'Active',
    toRule: 'SUPPLIER_EMAIL',
    createdAtUtc: '2026-09-28T00:00:00Z',
    publishedAtUtc: '2026-09-28T01:00:00Z',
    updatedAtUtc: null,
  },
]

function details(summary = summaries[0]!): EmailTemplate {
  return {
    ...summary,
    subjectTemplate: 'Purchase Order {{PurchaseOrderNumber}}',
    htmlBodyTemplate: '<h1>{{PurchaseOrderNumber}}</h1>{{ItemsTable}}',
    ccRecipients: null,
    bccRecipients: null,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    publishedByUserId: summary.status === 'Active' ? 4 : null,
    publishedByName: summary.status === 'Active' ? 'Demo Admin' : null,
    supportedPlaceholders: ['PurchaseOrderNumber', 'SupplierName', 'ItemsTable'],
  }
}

describe('EmailTemplateListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAll.mockResolvedValue(summaries)
    mocks.getById.mockImplementation(async (id) => details(summaries.find((item) => item.id === id)!))
    mocks.createVersion.mockResolvedValue({ ...details(summaries[0]), id: 3, version: 3 })
    mocks.update.mockImplementation(async (_id, payload) => ({ ...details(), ...payload }))
    mocks.preview.mockResolvedValue({
      recipientEmail: 'orders@abc-supplier.example',
      ccRecipients: null,
      bccRecipients: null,
      subject: 'Purchase Order PO-0001',
      htmlBody: '<h1>PO-0001</h1>',
    })
    mocks.publish.mockResolvedValue(details())
    mocks.delete.mockResolvedValue()
    vi.stubGlobal('confirm', vi.fn(() => true))
  })

  it('shows the correct actions for draft and active versions', async () => {
    const wrapper = mount(EmailTemplateListView)
    await flushPromises()

    expect(wrapper.get('h1').text()).toBe('Email Templates')
    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('Edit')
    expect(rows[0]!.text()).toContain('Publish')
    expect(rows[0]!.text()).toContain('Delete')
    expect(rows[1]!.text()).toContain('New version')
    expect(rows[1]!.text()).not.toContain('Edit')
  })

  it('opens the draft editor', async () => {
    const wrapper = mount(EmailTemplateListView)
    await flushPromises()

    const editButton = wrapper.findAll('button').find((button) => button.text() === 'Edit')
    await editButton?.trigger('click')
    await flushPromises()

    expect(mocks.getById).toHaveBeenCalledWith(2)
    expect(wrapper.findComponent(EmailTemplateEditor).exists()).toBe(true)
  })

  it('renders a fixed-data preview', async () => {
    const wrapper = mount(EmailTemplateListView)
    await flushPromises()

    const previewButton = wrapper.findAll('button').find((button) => button.text() === 'Preview')
    await previewButton?.trigger('click')
    await flushPromises()

    expect(mocks.preview).toHaveBeenCalled()
    expect(wrapper.findComponent(EmailTemplatePreview).exists()).toBe(true)
    expect(wrapper.get('.email-template-preview-subject').text()).toContain('PO-0001')
  })

  it('creates a new draft version from the active version', async () => {
    const wrapper = mount(EmailTemplateListView)
    await flushPromises()

    const button = wrapper.findAll('button').find((item) => item.text() === 'New version')
    await button?.trigger('click')
    await flushPromises()

    expect(mocks.createVersion).toHaveBeenCalledWith(1)
    expect(wrapper.findComponent(EmailTemplateEditor).exists()).toBe(true)
  })
})
