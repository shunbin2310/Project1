export type EmailDeliveryStatus = 'Pending' | 'Sent' | 'Failed'

export interface EmailAttachment {
  id: number
  fileName: string
  contentType: string
  fileSizeBytes: number
  createdAtUtc: string
}

export interface EmailRecordSummary {
  id: number
  sourceType: string
  sourceId: number
  sourceReference: string
  fromAddress: string
  fromName: string
  recipientEmail: string
  ccRecipients: string | null
  bccRecipients: string | null
  subject: string
  templateCode: string | null
  templateVersion: number | null
  status: EmailDeliveryStatus
  attemptCount: number
  createdByUserId: number | null
  createdByName: string | null
  resentFromEmailOutboxId: number | null
  createdAtUtc: string
  updatedAtUtc: string | null
  lastAttemptAtUtc: string | null
  sentAtUtc: string | null
  lastError: string | null
}

export interface EmailRecordDetails extends EmailRecordSummary {
  htmlBody: string
  attachments: EmailAttachment[]
}

export interface EmailRecordFilters {
  search?: string
  status?: EmailDeliveryStatus
  sourceType?: string
  createdFrom?: string
  createdTo?: string
}
