import type { ApplicationRole } from '@/types/auth'

export interface User {
  id: number
  email: string
  fullName: string
  departmentId: number | null
  departmentCode: string | null
  departmentName: string | null
  isActive: boolean
  roles: ApplicationRole[]
  createdAtUtc: string
}

export interface CreateUserRequest {
  email: string
  fullName: string
  departmentId: number | null
  roles: ApplicationRole[]
}

export interface UpdateUserRequest {
  fullName: string
  departmentId: number | null
  roles: ApplicationRole[]
}

export type UserFormValues = CreateUserRequest
