export interface UnitOfMeasure {
  id: number
  code: string
  name: string
  description: string | null
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

export interface CreateUnitOfMeasureRequest {
  code: string
  name: string
  description: string | null
}

export interface UpdateUnitOfMeasureRequest {
  name: string
  description: string | null
  isActive: boolean
}

export interface UnitOfMeasureFormValues extends UpdateUnitOfMeasureRequest {
  code: string
}
