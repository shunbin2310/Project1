export interface ProductCategory {
  id: number
  code: string
  name: string
  description: string | null
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

export interface CreateProductCategoryRequest {
  name: string
  description: string | null
}

export interface UpdateProductCategoryRequest {
  name: string
  description: string | null
  isActive: boolean
}

export type ProductCategoryFormValues = UpdateProductCategoryRequest
