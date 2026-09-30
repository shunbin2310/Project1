<script setup lang="ts">
import type { EmailAttachment, EmailRecordDetails } from '@/types/emailRecord'

defineProps<{
  emailRecord: EmailRecordDetails
  busy?: boolean
}>()

const emit = defineEmits<{
  close: []
  retry: []
  resend: []
  viewAttachment: [attachment: EmailAttachment]
  downloadAttachment: [attachment: EmailAttachment]
}>()

function formatDateTime(value: string | null) {
  if (!value) return 'Not available'
  return new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function formatFileSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
</script>

<template>
  <div class="modal-backdrop" @click.self="!busy && emit('close')">
    <section
      class="modal-card email-record-details-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="email-record-details-title"
    >
      <header class="modal-header email-record-details-header">
        <div>
          <p class="eyebrow">Email record #{{ emailRecord.id }}</p>
          <h2 id="email-record-details-title">{{ emailRecord.subject }}</h2>
          <p>{{ emailRecord.sourceType }} · {{ emailRecord.sourceReference }}</p>
        </div>
        <button
          class="icon-button"
          type="button"
          aria-label="Close email record"
          :disabled="busy"
          @click="emit('close')"
        >
          &times;
        </button>
      </header>

      <div class="email-record-details">
        <section class="email-record-overview" aria-label="Email delivery overview">
          <div>
            <span>Status</span>
            <strong
              class="email-record-status"
              :class="`email-${emailRecord.status.toLowerCase()}`"
            >
              {{ emailRecord.status }}
            </strong>
          </div>
          <div>
            <span>Attempts</span>
            <strong>{{ emailRecord.attemptCount }}</strong>
          </div>
          <div>
            <span>Created</span>
            <strong>{{ formatDateTime(emailRecord.createdAtUtc) }}</strong>
          </div>
          <div>
            <span>Sent</span>
            <strong>{{ formatDateTime(emailRecord.sentAtUtc) }}</strong>
          </div>
        </section>

        <section class="email-record-addresses" aria-label="Email addresses">
          <div>
            <span>From</span>
            <strong>{{ emailRecord.fromName }}</strong>
            <small>{{ emailRecord.fromAddress }}</small>
          </div>
          <div>
            <span>To</span>
            <strong>{{ emailRecord.recipientEmail }}</strong>
          </div>
          <div>
            <span>CC</span>
            <strong>{{ emailRecord.ccRecipients || 'None' }}</strong>
          </div>
          <div>
            <span>BCC</span>
            <strong>{{ emailRecord.bccRecipients || 'None' }}</strong>
          </div>
        </section>

        <div v-if="emailRecord.resentFromEmailOutboxId" class="email-record-notice">
          Resent from email record #{{ emailRecord.resentFromEmailOutboxId }}.
        </div>

        <div class="email-record-notice">
          <strong>Template:</strong>
          <template v-if="emailRecord.templateCode && emailRecord.templateVersion">
            {{ emailRecord.templateCode }} · Version {{ emailRecord.templateVersion }}
          </template>
          <template v-else>Legacy email</template>
        </div>

        <div v-if="emailRecord.lastError" class="email-record-error" role="alert">
          <strong>Latest delivery error</strong>
          <p>{{ emailRecord.lastError }}</p>
        </div>

        <section class="email-record-attachments" aria-labelledby="email-attachments-title">
          <div class="email-record-section-heading">
            <div>
              <strong id="email-attachments-title">Attachments</strong>
              <span>Files saved with this email snapshot</span>
            </div>
            <span>{{ emailRecord.attachments.length }}</span>
          </div>

          <div v-if="emailRecord.attachments.length" class="email-attachment-list">
            <article
              v-for="attachment in emailRecord.attachments"
              :key="attachment.id"
              class="email-attachment-item"
            >
              <span class="email-attachment-icon" aria-hidden="true">PDF</span>
              <div>
                <strong>{{ attachment.fileName }}</strong>
                <span>{{ formatFileSize(attachment.fileSizeBytes) }} · {{ attachment.contentType }}</span>
              </div>
              <div class="email-attachment-actions">
                <button
                  class="text-button"
                  type="button"
                  :disabled="busy"
                  @click="emit('viewAttachment', attachment)"
                >
                  View PDF
                </button>
                <button
                  class="text-button"
                  type="button"
                  :disabled="busy"
                  @click="emit('downloadAttachment', attachment)"
                >
                  Download PDF
                </button>
              </div>
            </article>
          </div>
          <p v-else class="email-attachment-empty">This email record has no attachments.</p>
        </section>

        <section class="email-record-content">
          <div>
            <strong>Email content</strong>
            <span>Saved HTML snapshot</span>
          </div>
          <iframe title="Email content" sandbox="" :srcdoc="emailRecord.htmlBody"></iframe>
        </section>

        <footer class="modal-actions">
          <button class="button button-secondary" type="button" :disabled="busy" @click="emit('close')">
            Close
          </button>
          <button
            v-if="emailRecord.status === 'Failed'"
            class="button button-primary"
            type="button"
            :disabled="busy"
            @click="emit('retry')"
          >
            {{ busy ? 'Queuing...' : 'Retry email' }}
          </button>
          <button
            v-if="emailRecord.status === 'Sent'"
            class="button button-primary"
            type="button"
            :disabled="busy"
            @click="emit('resend')"
          >
            {{ busy ? 'Queuing...' : 'Resend email' }}
          </button>
        </footer>
      </div>
    </section>
  </div>
</template>
