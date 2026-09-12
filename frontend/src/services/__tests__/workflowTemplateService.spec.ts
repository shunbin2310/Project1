import { afterEach, describe, expect, it, vi } from 'vitest'

import type { CreateWorkflowTemplateRequest } from '@/types/workflowTemplate'
import { workflowTemplateService } from '../workflowTemplateService'

const definition: CreateWorkflowTemplateRequest = {
  code: 'TEST_APPROVAL',
  name: 'Test Approval',
  entityType: 'TestRecord',
  steps: [
    {
      code: 'DRAFT',
      name: 'Draft',
      displayOrder: 1,
      isInitial: true,
      isTerminal: false,
      actions: [
        {
          code: 'SUBMIT',
          name: 'Submit',
          toStepCode: 'APPROVED',
          requiresComment: false,
          actioners: [{ actionerType: 'Requester', actionerKey: null }],
        },
      ],
    },
    {
      code: 'APPROVED',
      name: 'Approved',
      displayOrder: 2,
      isInitial: false,
      isTerminal: true,
      actions: [],
    },
  ],
}

describe('workflowTemplateService', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests a filtered template list', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await workflowTemplateService.getAll('PURCHASE_REQUEST')

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/workflow-templates?code=PURCHASE_REQUEST',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('sends the full definition when creating a template', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify({ id: 8, ...definition, version: 1, steps: [] }), {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await workflowTemplateService.create(definition)

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5165/api/workflow-templates',
      expect.objectContaining({ method: 'POST', body: JSON.stringify(definition) }),
    )
  })

  it('uses dedicated endpoints for versioning, publishing, and deleting', async () => {
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ id: 9 }), {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        }),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ id: 9 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      )
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await workflowTemplateService.createVersion(4)
    await workflowTemplateService.publish(9)
    await workflowTemplateService.delete(9)

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      'http://localhost:5165/api/workflow-templates/4/versions',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      'http://localhost:5165/api/workflow-templates/9/publish',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      'http://localhost:5165/api/workflow-templates/9',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
