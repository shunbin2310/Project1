import { apiBlobRequest, apiRequest, ApiError } from '@/services/apiClient'
import type {
  EmailRecordDetails,
  EmailRecordFilters,
  EmailRecordSummary,
} from '@/types/emailRecord'

export class EmailRecordApiError extends ApiError {
  constructor(status: number, message: string) {
    super(status, message)
    this.name = 'EmailRecordApiError'
  }
}

const request = <T>(path: string, options?: RequestInit) =>
  apiRequest<T>(path, options, EmailRecordApiError)

export const emailRecordService = {
  getAll(filters: EmailRecordFilters = {}) {
    const query = new URLSearchParams()
    if (filters.search?.trim()) query.set('search', filters.search.trim())
    if (filters.status) query.set('status', filters.status)
    if (filters.sourceType) query.set('sourceType', filters.sourceType)
    if (filters.createdFrom) query.set('createdFrom', filters.createdFrom)
    if (filters.createdTo) query.set('createdTo', filters.createdTo)

    const queryString = query.toString()
    return request<EmailRecordSummary[]>(
      `/api/email-records${queryString ? `?${queryString}` : ''}`,
    )
  },

  getById(id: number) {
    return request<EmailRecordDetails>(`/api/email-records/${id}`)
  },

  retry(id: number) {
    return request<EmailRecordDetails>(`/api/email-records/${id}/retry`, { method: 'POST' })
  },

  resend(id: number) {
    return request<EmailRecordDetails>(`/api/email-records/${id}/resend`, { method: 'POST' })
  },

  viewAttachment(emailRecordId: number, attachmentId: number) {
    return apiBlobRequest(
      `/api/email-records/${emailRecordId}/attachments/${attachmentId}/view`,
      undefined,
      EmailRecordApiError,
    )
  },

  downloadAttachment(emailRecordId: number, attachmentId: number) {
    return apiBlobRequest(
      `/api/email-records/${emailRecordId}/attachments/${attachmentId}/download`,
      undefined,
      EmailRecordApiError,
    )
  },
}
