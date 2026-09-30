export type EmailTemplateStatus = 'Draft' | 'Active' | 'Superseded'

export interface EmailTemplateSummary {
  id: number
  code: string
  name: string
  version: number
  status: EmailTemplateStatus
  toRule: string
  createdAtUtc: string
  publishedAtUtc: string | null
  updatedAtUtc: string | null
}

export interface EmailTemplate extends EmailTemplateSummary {
  subjectTemplate: string
  htmlBodyTemplate: string
  ccRecipients: string | null
  bccRecipients: string | null
  createdByUserId: number | null
  createdByName: string | null
  publishedByUserId: number | null
  publishedByName: string | null
  supportedPlaceholders: string[]
}

export interface UpdateEmailTemplateRequest {
  name: string
  subjectTemplate: string
  htmlBodyTemplate: string
  ccRecipients: string | null
  bccRecipients: string | null
}

export interface PreviewEmailTemplateRequest {
  subjectTemplate: string
  htmlBodyTemplate: string
  ccRecipients: string | null
  bccRecipients: string | null
}

export interface EmailTemplatePreview {
  recipientEmail: string
  ccRecipients: string | null
  bccRecipients: string | null
  subject: string
  htmlBody: string
}
