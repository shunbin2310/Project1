<script setup lang="ts">
import type { EmailRecordDetails } from '@/types/emailRecord'

defineProps<{
  emailRecord: EmailRecordDetails
  busy?: boolean
}>()

const emit = defineEmits<{
  close: []
  retry: []
  resend: []
}>()

function formatDateTime(value: string | null) {
  if (!value) return 'Not available'
  return new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
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
