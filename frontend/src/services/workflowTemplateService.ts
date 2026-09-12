import { apiRequest, ApiError as BaseApiError } from '@/services/apiClient'
import type {
  CreateWorkflowTemplateRequest,
  UpdateWorkflowTemplateRequest,
  WorkflowTemplate,
  WorkflowTemplateSummary,
} from '@/types/workflowTemplate'

export class ApiError extends BaseApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'ApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) => apiRequest<T>(path, options, ApiError)

export const workflowTemplateService = {
  getAll(code = ''): Promise<WorkflowTemplateSummary[]> {
    const query = code.trim() ? `?code=${encodeURIComponent(code.trim())}` : ''
    return request<WorkflowTemplateSummary[]>(`/api/workflow-templates${query}`)
  },

  getById(id: number): Promise<WorkflowTemplate> {
    return request<WorkflowTemplate>(`/api/workflow-templates/${id}`)
  },

  create(payload: CreateWorkflowTemplateRequest): Promise<WorkflowTemplate> {
    return request<WorkflowTemplate>('/api/workflow-templates', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  createVersion(id: number): Promise<WorkflowTemplate> {
    return request<WorkflowTemplate>(`/api/workflow-templates/${id}/versions`, {
      method: 'POST',
    })
  },

  update(id: number, payload: UpdateWorkflowTemplateRequest): Promise<WorkflowTemplate> {
    return request<WorkflowTemplate>(`/api/workflow-templates/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  publish(id: number): Promise<WorkflowTemplate> {
    return request<WorkflowTemplate>(`/api/workflow-templates/${id}/publish`, {
      method: 'POST',
    })
  },

  delete(id: number): Promise<void> {
    return request<void>(`/api/workflow-templates/${id}`, { method: 'DELETE' })
  },
}
