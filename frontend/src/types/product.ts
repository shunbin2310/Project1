export interface Product {
  id: number
  code: string
  name: string
  description: string | null
  storageLocation: string | null
  defaultUnitPrice: number
  reorderLevel: number
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

export interface CreateProductRequest {
  name: string
  description: string | null
  storageLocation?: string | null
  defaultUnitPrice: number
  reorderLevel: number
}

export interface UpdateProductRequest extends CreateProductRequest {
  isActive: boolean
}

export type ProductFormValues = UpdateProductRequest
