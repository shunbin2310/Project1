import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import type { EmailRecordDetails, EmailRecordSummary } from '@/types/emailRecord'
import EmailRecordListView from '../EmailRecordListView.vue'

const mocks = vi.hoisted(() => ({
  getAll: vi.fn<() => Promise<EmailRecordSummary[]>>(),
  getById: vi.fn<(id: number) => Promise<EmailRecordDetails>>(),
  retry: vi.fn<(id: number) => Promise<EmailRecordDetails>>(),
  resend: vi.fn<(id: number) => Promise<EmailRecordDetails>>(),
}))

vi.mock('@/services/emailRecordService', () => ({
  emailRecordService: mocks,
}))

function emailRecord(
  id: number,
  status: EmailRecordSummary['status'],
): EmailRecordDetails {
  return {
    id,
    sourceType: 'PurchaseOrder',
    sourceId: 5,
    sourceReference: 'PO-0005',
    fromAddress: 'purchasing@project1.test',
    fromName: 'Project1 Purchasing',
    recipientEmail: 'orders@supplier.test',
    ccRecipients: null,
    bccRecipients: null,
    subject: 'Purchase Order PO-0005',
    htmlBody: '<h1>PO-0005</h1>',
    templateCode: 'PURCHASE_ORDER_ISSUED',
    templateVersion: 1,
    status,
    attemptCount: status === 'Pending' ? 0 : 1,
    createdByUserId: 4,
    createdByName: 'Demo Admin',
    resentFromEmailOutboxId: null,
    createdAtUtc: '2026-09-28T08:00:00Z',
    updatedAtUtc: null,
    lastAttemptAtUtc: status === 'Pending' ? null : '2026-09-28T08:01:00Z',
    sentAtUtc: status === 'Sent' ? '2026-09-28T08:01:00Z' : null,
    lastError: status === 'Failed' ? 'SMTP unavailable' : null,
  }
}

describe('EmailRecordListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAll.mockResolvedValue([emailRecord(1, 'Sent'), emailRecord(2, 'Failed')])
    mocks.getById.mockImplementation(async (id) => emailRecord(id, id === 1 ? 'Sent' : 'Failed'))
    mocks.retry.mockResolvedValue(emailRecord(2, 'Pending'))
    mocks.resend.mockResolvedValue({
      ...emailRecord(3, 'Pending'),
      resentFromEmailOutboxId: 1,
    })
    vi.stubGlobal(
      'confirm',
      vi.fn(() => true),
    )
  })

  it('loads records and shows the delivery summary', async () => {
    const wrapper = mount(EmailRecordListView)
    await flushPromises()

    expect(mocks.getAll).toHaveBeenCalled()
    expect(wrapper.get('h1').text()).toBe('Email Records')
    expect(wrapper.findAll('.summary-card')[0]?.text()).toContain('1')
    expect(wrapper.findAll('.summary-card')[2]?.text()).toContain('1')
    expect(wrapper.get('tbody').text()).toContain('PO-0005')
    expect(wrapper.get('tbody').text()).toContain('orders@supplier.test')
  })

  it('opens a failed record and queues a retry', async () => {
    const wrapper = mount(EmailRecordListView)
    await flushPromises()

    await wrapper.findAll('tbody button')[1]?.trigger('click')
    await flushPromises()
    expect(mocks.getById).toHaveBeenCalledWith(2)
    expect(wrapper.get('[role="dialog"]').text()).toContain('SMTP unavailable')

    await wrapper
      .findAll('[role="dialog"] button')
      .find((button) => button.text() === 'Retry email')
      ?.trigger('click')
    await flushPromises()

    expect(mocks.retry).toHaveBeenCalledWith(2)
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(wrapper.get('.success-toast').text()).toContain('queued')
  })

  it('resends a sent record as a new email record', async () => {
    const wrapper = mount(EmailRecordListView)
    await flushPromises()

    await wrapper.findAll('tbody button')[0]?.trigger('click')
    await flushPromises()
    await wrapper
      .findAll('[role="dialog"] button')
      .find((button) => button.text() === 'Resend email')
      ?.trigger('click')
    await flushPromises()

    expect(window.confirm).toHaveBeenCalled()
    expect(mocks.resend).toHaveBeenCalledWith(1)
    expect(wrapper.get('.success-toast').text()).toContain('Email #3')
  })
})
