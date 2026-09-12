export interface SupplierProduct {
  id: number
  supplierId: number
  supplierCode: string
  supplierName: string
  productId: number
  productCode: string
  productName: string
  unitOfMeasureCode: string
  unitOfMeasureName: string
  productDefaultUnitPrice: number
  isPreferred: boolean
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

export interface CreateSupplierProductRequest {
  supplierId: number
  productId: number
  isPreferred: boolean
}

export interface UpdateSupplierProductRequest {
  isPreferred: boolean
  isActive: boolean
}

export interface SupplierProductFormValues extends CreateSupplierProductRequest {
  isActive: boolean
}

export interface SupplierProductFilters {
  supplierId?: number
  productId?: number
  includeInactive?: boolean
}
