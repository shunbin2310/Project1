import { afterEach, describe, expect, it, vi } from 'vitest'

import { emailTemplateService } from '../emailTemplateService'

describe('emailTemplateService', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('calls version, update, preview, publish, and delete endpoints', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(
      async () =>
        new Response('{}', {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
    )
    vi.stubGlobal('fetch', fetchMock)
    const payload = {
      name: 'Purchase Order Issued Email',
      subjectTemplate: 'Purchase Order {{PurchaseOrderNumber}}',
      htmlBodyTemplate: '<h1>{{PurchaseOrderNumber}}</h1>',
      ccRecipients: null,
      bccRecipients: null,
    }

    await emailTemplateService.createVersion(1)
    await emailTemplateService.update(2, payload)
    await emailTemplateService.preview(payload)
    await emailTemplateService.publish(2)
    await emailTemplateService.delete(2)

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      'http://localhost:5165/api/email-templates/1/versions',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      'http://localhost:5165/api/email-templates/2',
      expect.objectContaining({ method: 'PUT' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      'http://localhost:5165/api/email-templates/preview',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      4,
      'http://localhost:5165/api/email-templates/2/publish',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      5,
      'http://localhost:5165/api/email-templates/2',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
