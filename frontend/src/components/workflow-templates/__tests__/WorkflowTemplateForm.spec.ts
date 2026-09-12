import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { WorkflowTemplate, WorkflowTemplateFormValues } from '@/types/workflowTemplate'
import WorkflowTemplateForm from '../WorkflowTemplateForm.vue'

const publishedTemplate: WorkflowTemplate = {
  id: 1,
  code: 'PURCHASE_REQUEST',
  name: 'Purchase Request Approval',
  entityType: 'PurchaseRequest',
  version: 1,
  isPublished: true,
  isActive: true,
  createdAtUtc: '2026-08-30T00:00:00Z',
  publishedAtUtc: '2026-08-30T01:00:00Z',
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

describe('WorkflowTemplateForm', () => {
  it('creates a valid two-step draft definition', async () => {
    const wrapper = mount(WorkflowTemplateForm, {
      props: { template: null, users: [], saving: false, errorMessage: '' },
    })

    await wrapper.get('#workflow-template-code').setValue('interview-approval')
    await wrapper.get('#workflow-template-name').setValue('Interview Approval')
    await wrapper.get('#workflow-template-entity').setValue('InterviewRecord')
    await wrapper.get('form').trigger('submit')

    const values = wrapper.emitted('save')?.[0]?.[0] as WorkflowTemplateFormValues | undefined
    expect(values).toMatchObject({
      code: 'INTERVIEW_APPROVAL',
      name: 'Interview Approval',
      entityType: 'InterviewRecord',
    })
    expect(values?.steps).toHaveLength(2)
    expect(values?.steps[0]).toMatchObject({
      code: 'DRAFT',
      displayOrder: 1,
      isInitial: true,
    })
    expect(values?.steps[0]?.actions[0]).toMatchObject({
      code: 'SUBMIT',
      toStepCode: 'APPROVED',
      actioners: [{ actionerType: 'Requester', actionerKey: null }],
    })
  })

  it('keeps code and entity type read-only when editing a version', () => {
    const wrapper = mount(WorkflowTemplateForm, {
      props: {
        template: { ...publishedTemplate, id: 2, version: 2, isPublished: false, isActive: false },
        users: [],
        saving: false,
        errorMessage: '',
      },
    })

    expect(wrapper.get('#workflow-template-code').attributes('readonly')).toBeDefined()
    expect(wrapper.get('#workflow-template-entity').attributes('readonly')).toBeDefined()
    expect(wrapper.get('#workflow-template-name').element).toHaveProperty(
      'value',
      'Purchase Request Approval',
    )
    expect(wrapper.text()).toContain('Save draft')
  })

  it('requires a non-terminal step to contain an action', async () => {
    const wrapper = mount(WorkflowTemplateForm, {
      props: { template: null, users: [], saving: false, errorMessage: '' },
    })

    await wrapper.get('#workflow-template-code').setValue('TEST_FLOW')
    await wrapper.get('#workflow-template-name').setValue('Test Flow')
    await wrapper.get('#workflow-template-entity').setValue('TestRecord')
    const removeAction = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Remove action')
    await removeAction?.trigger('click')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')).toBeUndefined()
    expect(wrapper.get('[role="alert"]').text()).toContain(
      'Non-terminal step DRAFT needs at least one action.',
    )
  })
})
