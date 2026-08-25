export interface Department {
  id: number
  code: string
  name: string
  description: string | null
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

export interface CreateDepartmentRequest {
  code: string
  name: string
  description: string | null
}

export interface UpdateDepartmentRequest {
  name: string
  description: string | null
  isActive: boolean
}

export interface DepartmentFormValues extends UpdateDepartmentRequest {
  code: string
}
