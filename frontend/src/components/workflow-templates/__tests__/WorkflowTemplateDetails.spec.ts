import { describe, expect, it } from 'vitest'

import { mount } from '@vue/test-utils'
import type { WorkflowTemplate, WorkflowTemplateStep } from '@/types/workflowTemplate'
import WorkflowTemplateDetails from '../WorkflowTemplateDetails.vue'

function step(
  id: number,
  code: string,
  name: string,
  displayOrder: number,
  targetCodes: string[],
  options: { initial?: boolean; terminal?: boolean } = {},
): WorkflowTemplateStep {
  return {
    id,
    code,
    name,
    displayOrder,
    isInitial: options.initial ?? false,
    isTerminal: options.terminal ?? false,
    actions: targetCodes.map((targetCode, index) => ({
      id: id * 10 + index,
      code: `ACTION_${index}`,
      name: `Action ${index}`,
      toStepCode: targetCode,
      requiresComment: false,
      actioners: [{ id: id * 100 + index, actionerType: 'Role', actionerKey: 'ADMIN' }],
    })),
  }
}

const template: WorkflowTemplate = {
  id: 2,
  code: 'PURCHASE_REQUEST',
  name: 'Purchase Request Approval',
  entityType: 'PurchaseRequest',
  version: 2,
  isPublished: false,
  isActive: false,
  createdAtUtc: '2026-09-01T00:00:00Z',
  publishedAtUtc: null,
  steps: [
    step(1, 'DRAFT', 'Draft', 1, ['DEPARTMENT_REVIEW'], { initial: true }),
    step(2, 'FINANCE_REVIEW', 'Finance Review', 2, ['APPROVED', 'REJECTED']),
    step(3, 'DEPARTMENT_REVIEW', 'Department Review', 3, ['FINANCE_REVIEW', 'REJECTED']),
    step(4, 'APPROVED', 'Approved', 4, [], { terminal: true }),
    step(5, 'REJECTED', 'Rejected', 5, [], { terminal: true }),
  ],
}

describe('WorkflowTemplateDetails', () => {
  it('displays steps in transition order instead of stored display order', () => {
    const wrapper = mount(WorkflowTemplateDetails, {
      props: { template, users: [] },
    })

    expect(
      wrapper
        .findAll('.workflow-step-content > header > div:first-child > strong')
        .map((heading) => heading.text()),
    ).toEqual(['Draft', 'Department Review', 'Finance Review', 'Approved', 'Rejected'])
    expect(wrapper.findAll('.workflow-step-rail > span').map((number) => number.text())).toEqual([
      '1',
      '2',
      '3',
      '4',
      '5',
    ])
  })
})
