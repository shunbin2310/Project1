export interface Supplier {
  id: number
  code: string
  name: string
  contactPerson: string | null
  email: string | null
  phone: string | null
  address: string | null
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

export interface CreateSupplierRequest {
  name: string
  contactPerson: string | null
  email: string | null
  phone: string | null
  address: string | null
}

export interface UpdateSupplierRequest {
  name: string
  contactPerson: string | null
  email: string | null
  phone: string | null
  address: string | null
  isActive: boolean
}

export type SupplierFormValues = UpdateSupplierRequest
