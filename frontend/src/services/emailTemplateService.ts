import { apiRequest, ApiError as BaseApiError } from '@/services/apiClient'
import type {
  EmailTemplate,
  EmailTemplatePreview,
  EmailTemplateSummary,
  PreviewEmailTemplateRequest,
  UpdateEmailTemplateRequest,
} from '@/types/emailTemplate'

export class ApiError extends BaseApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'ApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) => apiRequest<T>(path, options, ApiError)

export const emailTemplateService = {
  getAll(): Promise<EmailTemplateSummary[]> {
    return request<EmailTemplateSummary[]>('/api/email-templates')
  },

  getById(id: number): Promise<EmailTemplate> {
    return request<EmailTemplate>(`/api/email-templates/${id}`)
  },

  createVersion(id: number): Promise<EmailTemplate> {
    return request<EmailTemplate>(`/api/email-templates/${id}/versions`, { method: 'POST' })
  },

  update(id: number, payload: UpdateEmailTemplateRequest): Promise<EmailTemplate> {
    return request<EmailTemplate>(`/api/email-templates/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  preview(payload: PreviewEmailTemplateRequest): Promise<EmailTemplatePreview> {
    return request<EmailTemplatePreview>('/api/email-templates/preview', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  publish(id: number): Promise<EmailTemplate> {
    return request<EmailTemplate>(`/api/email-templates/${id}/publish`, { method: 'POST' })
  },

  delete(id: number): Promise<void> {
    return request<void>(`/api/email-templates/${id}`, { method: 'DELETE' })
  },
}
