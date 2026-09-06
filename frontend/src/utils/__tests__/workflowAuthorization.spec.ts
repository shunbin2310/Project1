import { describe, expect, it } from 'vitest'

import {
  getAuthorizedWorkflowActions,
  isWorkflowActionAuthorized,
  isWorkflowActionDirectlyAssignedToActor,
} from '@/utils/workflowAuthorization'
import type { WorkflowAvailableAction } from '@/types/purchaseRequest'

const departmentApproval: WorkflowAvailableAction = {
  code: 'APPROVE',
  name: 'Approve department review',
  requiresComment: false,
  toStepCode: 'FINANCE_REVIEW',
  toStepName: 'Finance Review',
  actioners: [{ actionerType: 'Role', actionerKey: 'DEPARTMENT_APPROVER' }],
}

describe('workflowAuthorization', () => {
  it('allows an ADMIN-only user to perform any workflow action', () => {
    const administrator = { id: 4, name: 'Demo Admin', roles: ['ADMIN'] }

    expect(isWorkflowActionAuthorized(departmentApproval, administrator)).toBe(true)
    expect(getAuthorizedWorkflowActions([departmentApproval], administrator)).toEqual([
      departmentApproval,
    ])
    expect(isWorkflowActionDirectlyAssignedToActor(departmentApproval, administrator)).toBe(true)
  })

  it('still rejects a user without the assigned role', () => {
    const requester = { id: 1, name: 'Demo Requester', roles: ['REQUESTER'] }

    expect(isWorkflowActionAuthorized(departmentApproval, requester)).toBe(false)
  })
})
