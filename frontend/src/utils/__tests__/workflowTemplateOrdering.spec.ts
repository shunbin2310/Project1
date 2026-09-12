import { describe, expect, it } from 'vitest'

import type { WorkflowTemplateStep } from '@/types/workflowTemplate'
import { orderWorkflowSteps } from '../workflowTemplateOrdering'

function step(
  id: number,
  code: string,
  displayOrder: number,
  targetCodes: string[],
  options: { initial?: boolean; terminal?: boolean } = {},
): WorkflowTemplateStep {
  return {
    id,
    code,
    name: code,
    displayOrder,
    isInitial: options.initial ?? false,
    isTerminal: options.terminal ?? false,
    actions: targetCodes.map((targetCode, index) => ({
      id: id * 10 + index,
      code: `ACTION_${index}`,
      name: `Action ${index}`,
      toStepCode: targetCode,
      requiresComment: false,
      actioners: [],
    })),
  }
}

describe('orderWorkflowSteps', () => {
  it('follows transitions even when display order places finance before department', () => {
    const steps = [
      step(1, 'DRAFT', 1, ['DEPARTMENT_REVIEW'], { initial: true }),
      step(2, 'FINANCE_REVIEW', 2, ['APPROVED', 'REJECTED']),
      step(3, 'DEPARTMENT_REVIEW', 3, ['FINANCE_REVIEW', 'REJECTED']),
      step(4, 'APPROVED', 4, [], { terminal: true }),
      step(5, 'REJECTED', 5, [], { terminal: true }),
    ]

    expect(orderWorkflowSteps(steps).map((item) => item.code)).toEqual([
      'DRAFT',
      'DEPARTMENT_REVIEW',
      'FINANCE_REVIEW',
      'APPROVED',
      'REJECTED',
    ])
  })

  it('uses display order as a tie breaker for independent branches', () => {
    const steps = [
      step(1, 'START', 1, ['REVIEW_A', 'REVIEW_B'], { initial: true }),
      step(2, 'REVIEW_B', 3, ['DONE_B']),
      step(3, 'REVIEW_A', 2, ['DONE_A']),
      step(4, 'DONE_B', 5, [], { terminal: true }),
      step(5, 'DONE_A', 4, [], { terminal: true }),
    ]

    expect(orderWorkflowSteps(steps).map((item) => item.code)).toEqual([
      'START',
      'REVIEW_A',
      'REVIEW_B',
      'DONE_A',
      'DONE_B',
    ])
  })

  it('keeps every step visible when an invalid cycle is returned', () => {
    const steps = [
      step(1, 'START', 1, ['DONE'], { initial: true }),
      step(2, 'CYCLE_A', 2, ['CYCLE_B']),
      step(3, 'CYCLE_B', 3, ['CYCLE_A']),
      step(4, 'DONE', 4, [], { terminal: true }),
    ]

    expect(orderWorkflowSteps(steps).map((item) => item.code)).toEqual([
      'START',
      'DONE',
      'CYCLE_A',
      'CYCLE_B',
    ])
  })
})
