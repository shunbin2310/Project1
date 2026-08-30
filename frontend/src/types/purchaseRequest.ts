export type WorkflowInstanceStatus = 'Running' | 'Completed'

export type WorkflowActionerType = 'Requester' | 'User' | 'Role'

export interface WorkflowActioner {
  actionerType: WorkflowActionerType
  actionerKey: string
}

export interface WorkflowAvailableAction {
  code: string
  name: string
  requiresComment: boolean
  toStepCode: string
  toStepName: string
  actioners: WorkflowActioner[]
}

export interface WorkflowHistoryEntry {
  id: number
  fromStepCode: string | null
  toStepCode: string
  actionCode: string
  actionBy: string
  comment: string | null
  actionAtUtc: string
}

export interface WorkflowInstance {
  id: number
  templateCode: string
  templateName: string
  templateVersion: number
  entityType: string
  entityId: number
  status: WorkflowInstanceStatus
  currentStepCode: string
  currentStepName: string
  startedAtUtc: string
  completedAtUtc: string | null
  availableActions: WorkflowAvailableAction[]
  history: WorkflowHistoryEntry[]
}

export interface PurchaseRequestItem {
  id: number
  productId: number
  productCode: string
  productName: string
  unitOfMeasureCode: string
  quantity: number
  estimatedUnitPrice: number
  lineTotal: number
}

export interface PurchaseRequest {
  id: number
  requestNumber: string
  requesterName: string | null
  departmentId: number | null
  departmentCode: string | null
  departmentName: string | null
  requiredDate: string | null
  justification: string | null
  estimatedTotal: number
  createdAtUtc: string
  updatedAtUtc: string | null
  items: PurchaseRequestItem[]
  workflow: WorkflowInstance
}

export interface PurchaseRequestItemInput {
  productId: number
  quantity: number
  estimatedUnitPrice: number | null
}

export interface PurchaseRequestFormValues {
  requesterName: string | null
  departmentId: number | null
  requiredDate: string | null
  justification: string | null
  items: PurchaseRequestItemInput[]
}

export type CreatePurchaseRequestRequest = PurchaseRequestFormValues

export type UpdatePurchaseRequestRequest = PurchaseRequestFormValues

export interface PurchaseRequestActionRequest {
  actionBy: string
  actorRoles: string[]
  comment: string | null
}

export interface WorkflowActorIdentity {
  key: 'requester' | 'department-approver' | 'finance-approver'
  label: string
  name: string
  roles: string[]
}
