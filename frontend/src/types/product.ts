export interface Product {
  id: number
  code: string
  name: string
  description: string | null
  productCategoryId: number
  productCategoryCode: string
  productCategoryName: string
  unitOfMeasureId: number
  unitOfMeasureCode: string
  unitOfMeasureName: string
  defaultUnitPrice: number
  reorderLevel: number
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

export interface CreateProductRequest {
  name: string
  description: string | null
  productCategoryId: number
  unitOfMeasureId: number
  defaultUnitPrice: number
  reorderLevel: number
}

export interface UpdateProductRequest extends CreateProductRequest {
  isActive: boolean
}

export type ProductFormValues = UpdateProductRequest
