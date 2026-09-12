import type { ApplicationRole } from '@/types/auth'

export type WorkflowActionerType = 'Requester' | 'User' | 'Role'

export interface WorkflowTemplateSummary {
  id: number
  code: string
  name: string
  entityType: string
  version: number
  isPublished: boolean
  isActive: boolean
  stepCount: number
  createdAtUtc: string
  publishedAtUtc: string | null
}

export interface WorkflowTemplateActioner {
  id: number
  actionerType: WorkflowActionerType
  actionerKey: string | null
}

export interface WorkflowTemplateAction {
  id: number
  code: string
  name: string
  toStepCode: string
  requiresComment: boolean
  actioners: WorkflowTemplateActioner[]
}

export interface WorkflowTemplateStep {
  id: number
  code: string
  name: string
  displayOrder: number
  isInitial: boolean
  isTerminal: boolean
  actions: WorkflowTemplateAction[]
}

export interface WorkflowTemplate {
  id: number
  code: string
  name: string
  entityType: string
  version: number
  isPublished: boolean
  isActive: boolean
  createdAtUtc: string
  publishedAtUtc: string | null
  steps: WorkflowTemplateStep[]
}

export interface WorkflowActionerDefinition {
  actionerType: WorkflowActionerType
  actionerKey: string | null
}

export interface WorkflowActionDefinition {
  code: string
  name: string
  toStepCode: string
  requiresComment: boolean
  actioners: WorkflowActionerDefinition[]
}

export interface WorkflowStepDefinition {
  code: string
  name: string
  displayOrder: number
  isInitial: boolean
  isTerminal: boolean
  actions: WorkflowActionDefinition[]
}

export interface CreateWorkflowTemplateRequest {
  code: string
  name: string
  entityType: string
  steps: WorkflowStepDefinition[]
}

export interface UpdateWorkflowTemplateRequest {
  name: string
  steps: WorkflowStepDefinition[]
}

export type WorkflowTemplateFormValues = CreateWorkflowTemplateRequest

export const workflowActionerTypes: readonly WorkflowActionerType[] = ['Requester', 'Role', 'User']

export const workflowActionerRoles: readonly ApplicationRole[] = [
  'REQUESTER',
  'DEPARTMENT_APPROVER',
  'FINANCE_APPROVER',
  'ADMIN',
]
