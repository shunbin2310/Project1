export const applicationRoles = {
  requester: 'REQUESTER',
  departmentApprover: 'DEPARTMENT_APPROVER',
  financeApprover: 'FINANCE_APPROVER',
  procurementOfficer: 'PROCUREMENT_OFFICER',
  purchaseOrderApprover: 'PURCHASE_ORDER_APPROVER',
  warehouseOfficer: 'WAREHOUSE_OFFICER',
  catalogManager: 'CATALOG_MANAGER',
  admin: 'ADMIN',
} as const

export type ApplicationRole = (typeof applicationRoles)[keyof typeof applicationRoles]

export interface AuthenticatedUser {
  id: number
  email: string
  fullName: string
  departmentId: number | null
  departmentCode: string | null
  departmentName: string | null
  roles: string[]
}

export interface LoginRequest {
  email: string
  password: string
}

export interface LoginResponse {
  accessToken: string
  expiresAtUtc: string
  user: AuthenticatedUser
}

export type AuthSession = LoginResponse
