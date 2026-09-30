<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import EmailRecordDetailsDialog from '@/components/email-records/EmailRecordDetails.vue'
import AppToast from '@/components/ui/AppToast.vue'
import { useToast } from '@/composables/useToast'
import { emailRecordService } from '@/services/emailRecordService'
import type {
  EmailDeliveryStatus,
  EmailAttachment,
  EmailRecordDetails,
  EmailRecordSummary,
} from '@/types/emailRecord'

const statusFilters: { value: '' | EmailDeliveryStatus; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Pending', label: 'Pending' },
  { value: 'Sent', label: 'Sent' },
  { value: 'Failed', label: 'Failed' },
]

const emailRecords = ref<EmailRecordSummary[]>([])
const loading = ref(true)
const loadError = ref('')
const operationError = ref('')
const search = ref('')
const selectedStatus = ref<'' | EmailDeliveryStatus>('')
const selectedSourceType = ref('')
const createdFrom = ref('')
const createdTo = ref('')
const selectedEmailRecord = ref<EmailRecordDetails | null>(null)
const busyEmailRecordId = ref<number | null>(null)
const { toast, showSuccess, dismissToast } = useToast()

const sentCount = computed(
  () => emailRecords.value.filter((record) => record.status === 'Sent').length,
)
const pendingCount = computed(
  () => emailRecords.value.filter((record) => record.status === 'Pending').length,
)
const failedCount = computed(
  () => emailRecords.value.filter((record) => record.status === 'Failed').length,
)
const sourceTypes = computed(() =>
  [...new Set(emailRecords.value.map((record) => record.sourceType))].sort(),
)
const visibleEmailRecords = computed(() => {
  const term = search.value.trim().toLowerCase()

  return emailRecords.value.filter((record) => {
    if (selectedStatus.value && record.status !== selectedStatus.value) return false
    if (selectedSourceType.value && record.sourceType !== selectedSourceType.value) return false
    if (createdFrom.value && record.createdAtUtc.slice(0, 10) < createdFrom.value) return false
    if (createdTo.value && record.createdAtUtc.slice(0, 10) > createdTo.value) return false
    if (!term) return true

    return [record.sourceReference, record.recipientEmail, record.subject].some((value) =>
      value.toLowerCase().includes(term),
    )
  })
})

onMounted(loadEmailRecords)

async function loadEmailRecords() {
  loading.value = true
  loadError.value = ''
  try {
    emailRecords.value = await emailRecordService.getAll()
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load email records.')
  } finally {
    loading.value = false
  }
}

async function openDetails(record: EmailRecordSummary) {
  busyEmailRecordId.value = record.id
  operationError.value = ''
  try {
    selectedEmailRecord.value = await emailRecordService.getById(record.id)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to load the email record.')
  } finally {
    busyEmailRecordId.value = null
  }
}

async function retryEmail() {
  const record = selectedEmailRecord.value
  if (!record) return

  busyEmailRecordId.value = record.id
  operationError.value = ''
  try {
    await emailRecordService.retry(record.id)
    selectedEmailRecord.value = null
    showSuccess(`Email #${record.id} was queued for another delivery attempt.`)
    await loadEmailRecords()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to retry the email.')
  } finally {
    busyEmailRecordId.value = null
  }
}

async function resendEmail() {
  const record = selectedEmailRecord.value
  if (!record) return

  const confirmed = window.confirm(
    `Resend ${record.subject} to ${record.recipientEmail}? A new email record will be created.`,
  )
  if (!confirmed) return

  busyEmailRecordId.value = record.id
  operationError.value = ''
  try {
    const resent = await emailRecordService.resend(record.id)
    selectedEmailRecord.value = null
    showSuccess(`Email #${resent.id} was created and queued for delivery.`)
    await loadEmailRecords()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to resend the email.')
  } finally {
    busyEmailRecordId.value = null
  }
}

async function viewAttachment(attachment: EmailAttachment) {
  const record = selectedEmailRecord.value
  if (!record) return

  const previewWindow = window.open('', '_blank')
  if (previewWindow) previewWindow.opener = null
  busyEmailRecordId.value = record.id
  operationError.value = ''
  try {
    const blob = await emailRecordService.viewAttachment(record.id, attachment.id)
    const objectUrl = URL.createObjectURL(blob)
    if (previewWindow) {
      previewWindow.location.href = objectUrl
    } else {
      window.open(objectUrl, '_blank', 'noopener,noreferrer')
    }
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000)
  } catch (error) {
    previewWindow?.close()
    operationError.value = getErrorMessage(error, 'Unable to open the PDF attachment.')
  } finally {
    busyEmailRecordId.value = null
  }
}

async function downloadAttachment(attachment: EmailAttachment) {
  const record = selectedEmailRecord.value
  if (!record) return

  busyEmailRecordId.value = record.id
  operationError.value = ''
  try {
    const blob = await emailRecordService.downloadAttachment(record.id, attachment.id)
    const objectUrl = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = objectUrl
    link.download = attachment.fileName
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(objectUrl)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to download the PDF attachment.')
  } finally {
    busyEmailRecordId.value = null
  }
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback
}

function formatDateTime(value: string | null) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function sourceTypeLabel(value: string) {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2')
}
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Communication audit</p>
        <h1>Email Records</h1>
        <p class="page-description">
          Review outgoing purchasing emails, investigate delivery failures, and safely retry or
          resend saved messages.
        </p>
      </div>
    </header>

    <div class="summary-grid" aria-label="Email delivery summary">
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Sent</span>
        <strong>{{ sentCount }}</strong>
        <span>Successfully delivered</span>
      </article>
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Pending</span>
        <strong>{{ pendingCount }}</strong>
        <span>Waiting for the background worker</span>
      </article>
      <article class="summary-card">
        <span class="summary-label">Failed</span>
        <strong>{{ failedCount }}</strong>
        <span>Need delivery attention</span>
      </article>
    </div>

    <AppToast :toast="toast" @dismiss="dismissToast" />

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">&times;</button>
    </div>

    <section class="data-panel" aria-labelledby="email-record-list-title">
      <div class="panel-toolbar email-record-toolbar">
        <div>
          <h2 id="email-record-list-title">Outgoing email register</h2>
          <p>{{ visibleEmailRecords.length }} records shown</p>
        </div>

        <div class="toolbar-actions email-record-filters">
          <label class="search-control">
            <span class="sr-only">Search email records</span>
            <span aria-hidden="true">⌕</span>
            <input v-model="search" type="search" placeholder="Search reference, recipient, or subject" />
          </label>
          <label class="relationship-filter-control">
            <span class="sr-only">Filter by delivery status</span>
            <select v-model="selectedStatus" aria-label="Filter by delivery status">
              <option v-for="status in statusFilters" :key="status.value" :value="status.value">
                {{ status.label }}
              </option>
            </select>
          </label>
          <label class="relationship-filter-control">
            <span class="sr-only">Filter by source</span>
            <select v-model="selectedSourceType" aria-label="Filter by source">
              <option value="">All sources</option>
              <option v-for="sourceType in sourceTypes" :key="sourceType" :value="sourceType">
                {{ sourceTypeLabel(sourceType) }}
              </option>
            </select>
          </label>
          <label class="email-record-date-filter">
            <span>From</span>
            <input v-model="createdFrom" type="date" :max="createdTo || undefined" />
          </label>
          <label class="email-record-date-filter">
            <span>To</span>
            <input v-model="createdTo" type="date" :min="createdFrom || undefined" />
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading email records</strong>
        <p>Retrieving delivery history and status.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Email records could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadEmailRecords">
          Try again
        </button>
      </div>

      <div v-else-if="!visibleEmailRecords.length" class="panel-state">
        <div class="empty-icon" aria-hidden="true">EM</div>
        <strong>{{ emailRecords.length ? 'No matching email records' : 'No emails have been queued' }}</strong>
        <p>Issue a Purchase Order to create the first outgoing email record.</p>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Source</th>
              <th>Recipient</th>
              <th>Subject</th>
              <th>Status</th>
              <th>Attempts</th>
              <th>Created</th>
              <th>Sent</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="record in visibleEmailRecords" :key="record.id">
              <td>
                <div class="email-record-identity">
                  <span class="code-avatar">EM</span>
                  <span>
                    <strong>{{ record.sourceReference }}</strong>
                    <small>#{{ record.id }} · {{ sourceTypeLabel(record.sourceType) }}</small>
                  </span>
                </div>
              </td>
              <td>
                <span class="table-primary">{{ record.recipientEmail }}</span>
                <small v-if="record.ccRecipients" class="table-secondary">CC: {{ record.ccRecipients }}</small>
              </td>
              <td class="email-record-subject">{{ record.subject }}</td>
              <td>
                <span class="email-record-status" :class="`email-${record.status.toLowerCase()}`">
                  {{ record.status }}
                </span>
              </td>
              <td>{{ record.attemptCount }}</td>
              <td>{{ formatDateTime(record.createdAtUtc) }}</td>
              <td>{{ formatDateTime(record.sentAtUtc) }}</td>
              <td>
                <div class="row-actions email-record-row-actions">
                  <button
                    class="text-button"
                    type="button"
                    :disabled="busyEmailRecordId === record.id"
                    @click="openDetails(record)"
                  >
                    View
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <EmailRecordDetailsDialog
      v-if="selectedEmailRecord"
      :email-record="selectedEmailRecord"
      :busy="busyEmailRecordId === selectedEmailRecord.id"
      @close="selectedEmailRecord = null"
      @retry="retryEmail"
      @resend="resendEmail"
      @view-attachment="viewAttachment"
      @download-attachment="downloadAttachment"
    />
  </section>
</template>
