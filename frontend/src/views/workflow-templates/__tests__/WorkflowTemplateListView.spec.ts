import { beforeEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import WorkflowTemplateDetails from '@/components/workflow-templates/WorkflowTemplateDetails.vue'
import WorkflowTemplateForm from '@/components/workflow-templates/WorkflowTemplateForm.vue'
import type { User } from '@/types/user'
import type {
  CreateWorkflowTemplateRequest,
  UpdateWorkflowTemplateRequest,
  WorkflowTemplate,
  WorkflowTemplateSummary,
} from '@/types/workflowTemplate'
import WorkflowTemplateListView from '../WorkflowTemplateListView.vue'

const mocks = vi.hoisted(() => ({
  getAll: vi.fn<() => Promise<WorkflowTemplateSummary[]>>(),
  getById: vi.fn<(id: number) => Promise<WorkflowTemplate>>(),
  createVersion: vi.fn<(id: number) => Promise<WorkflowTemplate>>(),
  update:
    vi.fn<(id: number, payload: UpdateWorkflowTemplateRequest) => Promise<WorkflowTemplate>>(),
  publish: vi.fn<(id: number) => Promise<WorkflowTemplate>>(),
  delete: vi.fn<(id: number) => Promise<void>>(),
  getUsers: vi.fn<() => Promise<User[]>>(),
}))

vi.mock('@/services/workflowTemplateService', () => ({
  ApiError: Error,
  workflowTemplateService: {
    getAll: mocks.getAll,
    getById: mocks.getById,
    create: vi.fn<(payload: CreateWorkflowTemplateRequest) => Promise<WorkflowTemplate>>(),
    createVersion: mocks.createVersion,
    update: mocks.update,
    publish: mocks.publish,
    delete: mocks.delete,
  },
}))

vi.mock('@/services/userService', () => ({
  userService: { getAll: mocks.getUsers },
}))

const summaries: WorkflowTemplateSummary[] = [
  {
    id: 2,
    code: 'PURCHASE_REQUEST',
    name: 'Purchase Request Approval',
    entityType: 'PurchaseRequest',
    version: 2,
    isPublished: false,
    isActive: false,
    stepCount: 2,
    createdAtUtc: '2026-09-01T00:00:00Z',
    publishedAtUtc: null,
  },
  {
    id: 1,
    code: 'PURCHASE_REQUEST',
    name: 'Purchase Request Approval',
    entityType: 'PurchaseRequest',
    version: 1,
    isPublished: true,
    isActive: true,
    stepCount: 2,
    createdAtUtc: '2026-08-30T00:00:00Z',
    publishedAtUtc: '2026-08-30T01:00:00Z',
  },
]

const template: WorkflowTemplate = {
  ...summaries[1]!,
  steps: [
    {
      id: 1,
      code: 'DRAFT',
      name: 'Draft',
      displayOrder: 1,
      isInitial: true,
      isTerminal: false,
      actions: [
        {
          id: 1,
          code: 'SUBMIT',
          name: 'Submit',
          toStepCode: 'APPROVED',
          requiresComment: false,
          actioners: [{ id: 1, actionerType: 'Requester', actionerKey: null }],
        },
      ],
    },
    {
      id: 2,
      code: 'APPROVED',
      name: 'Approved',
      displayOrder: 2,
      isInitial: false,
      isTerminal: true,
      actions: [],
    },
  ],
}

describe('WorkflowTemplateListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAll.mockResolvedValue(summaries)
    mocks.getUsers.mockResolvedValue([])
    mocks.getById.mockImplementation(async (id) => ({
      ...template,
      id,
      version: id === 2 ? 2 : 1,
      isPublished: id !== 2,
      isActive: id === 1,
    }))
    mocks.createVersion.mockResolvedValue({
      ...template,
      id: 3,
      version: 2,
      isPublished: false,
      isActive: false,
      publishedAtUtc: null,
    })
    mocks.publish.mockResolvedValue(template)
    mocks.delete.mockResolvedValue()
    vi.stubGlobal(
      'confirm',
      vi.fn(() => true),
    )
  })

  it('shows version-specific actions for draft and active templates', async () => {
    const wrapper = mount(WorkflowTemplateListView)
    await flushPromises()

    expect(wrapper.get('h1').text()).toBe('Workflow Templates')
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)

    const draftRow = wrapper.findAll('tbody tr')[0]!
    expect(draftRow.text()).toContain('Draft')
    expect(draftRow.text()).toContain('Edit')
    expect(draftRow.text()).toContain('Publish')
    expect(draftRow.text()).toContain('Delete')
    expect(draftRow.text()).not.toContain('New version')

    const activeRow = wrapper.findAll('tbody tr')[1]!
    expect(activeRow.text()).toContain('Active')
    expect(activeRow.text()).toContain('New version')
    expect(activeRow.text()).not.toContain('Edit')
    expect(activeRow.text()).not.toContain('Delete')
  })

  it('opens a read-only details dialog', async () => {
    const wrapper = mount(WorkflowTemplateListView)
    await flushPromises()

    const viewButton = wrapper.findAll('button').find((button) => button.text() === 'View')
    await viewButton?.trigger('click')
    await flushPromises()

    expect(mocks.getById).toHaveBeenCalledWith(2)
    expect(wrapper.findComponent(WorkflowTemplateDetails).exists()).toBe(true)
    expect(wrapper.get('[role="dialog"]').text()).toContain('Steps and transitions')
  })

  it('creates a new draft version and opens it for editing', async () => {
    const wrapper = mount(WorkflowTemplateListView)
    await flushPromises()

    const newVersionButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'New version')
    await newVersionButton?.trigger('click')
    await flushPromises()

    expect(mocks.createVersion).toHaveBeenCalledWith(1)
    expect(wrapper.findComponent(WorkflowTemplateForm).exists()).toBe(true)
    expect(wrapper.get('[role="dialog"]').text()).toContain('Edit PURCHASE_REQUEST version 2')
  })

  it('publishes a draft after confirmation and refreshes the list', async () => {
    const wrapper = mount(WorkflowTemplateListView)
    await flushPromises()

    const publishButton = wrapper.findAll('button').find((button) => button.text() === 'Publish')
    await publishButton?.trigger('click')
    await flushPromises()

    expect(window.confirm).toHaveBeenCalled()
    expect(mocks.publish).toHaveBeenCalledWith(2)
    expect(mocks.getAll).toHaveBeenCalledTimes(2)
    expect(wrapper.get('.success-toast').text()).toContain(
      'PURCHASE_REQUEST version 2 is now active.',
    )
  })

  it('deletes a draft after confirmation and refreshes the list', async () => {
    mocks.getAll.mockResolvedValueOnce(summaries).mockResolvedValueOnce([summaries[1]!])
    const wrapper = mount(WorkflowTemplateListView)
    await flushPromises()

    const deleteButton = wrapper.findAll('button').find((button) => button.text() === 'Delete')
    await deleteButton?.trigger('click')
    await flushPromises()

    expect(window.confirm).toHaveBeenCalled()
    expect(mocks.delete).toHaveBeenCalledWith(2)
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    expect(wrapper.get('.success-toast').text()).toContain(
      'PURCHASE_REQUEST version 2 was deleted.',
    )
  })

  it('shows the API message when a draft cannot be deleted', async () => {
    mocks.delete.mockRejectedValueOnce(new Error('Draft could not be deleted.'))
    const wrapper = mount(WorkflowTemplateListView)
    await flushPromises()

    const deleteButton = wrapper.findAll('button').find((button) => button.text() === 'Delete')
    await deleteButton?.trigger('click')
    await flushPromises()

    expect(wrapper.get('.alert-error').text()).toContain('Draft could not be deleted.')
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)
  })
})
