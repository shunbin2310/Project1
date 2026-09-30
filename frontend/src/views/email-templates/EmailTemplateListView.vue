<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import EmailTemplateEditor from '@/components/email-templates/EmailTemplateEditor.vue'
import EmailTemplatePreview from '@/components/email-templates/EmailTemplatePreview.vue'
import AppToast from '@/components/ui/AppToast.vue'
import { useToast } from '@/composables/useToast'
import { ApiError, emailTemplateService } from '@/services/emailTemplateService'
import type {
  EmailTemplate,
  EmailTemplatePreview as EmailTemplatePreviewModel,
  EmailTemplateStatus,
  EmailTemplateSummary,
  PreviewEmailTemplateRequest,
  UpdateEmailTemplateRequest,
} from '@/types/emailTemplate'

type StatusFilter = 'All' | EmailTemplateStatus

const templates = ref<EmailTemplateSummary[]>([])
const loading = ref(true)
const saving = ref(false)
const previewing = ref(false)
const busyTemplateId = ref<number | null>(null)
const search = ref('')
const statusFilter = ref<StatusFilter>('All')
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const editingTemplate = ref<EmailTemplate | null>(null)
const preview = ref<EmailTemplatePreviewModel | null>(null)
const previewVersion = ref(1)
const { toast, showSuccess, dismissToast } = useToast()

const activeCount = computed(
  () => templates.value.filter((template) => template.status === 'Active').length,
)
const draftCount = computed(
  () => templates.value.filter((template) => template.status === 'Draft').length,
)
const supersededCount = computed(
  () => templates.value.filter((template) => template.status === 'Superseded').length,
)
const visibleTemplates = computed(() => {
  const term = search.value.trim().toLowerCase()
  return templates.value.filter((template) => {
    if (statusFilter.value !== 'All' && template.status !== statusFilter.value) return false
    if (!term) return true
    return [template.code, template.name, `version ${template.version}`].some((value) =>
      value.toLowerCase().includes(term),
    )
  })
})

onMounted(loadTemplates)

async function loadTemplates() {
  loading.value = true
  loadError.value = ''
  try {
    templates.value = await emailTemplateService.getAll()
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load email templates.')
  } finally {
    loading.value = false
  }
}

async function reloadTemplates() {
  templates.value = await emailTemplateService.getAll()
}

async function editTemplate(template: EmailTemplateSummary) {
  if (template.status !== 'Draft') return
  operationError.value = ''
  formError.value = ''
  busyTemplateId.value = template.id
  try {
    editingTemplate.value = await emailTemplateService.getById(template.id)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to load the email template draft.')
  } finally {
    busyTemplateId.value = null
  }
}

async function createVersion(template: EmailTemplateSummary) {
  const confirmed = window.confirm(
    `Create a new draft from ${template.code} version ${template.version}?`,
  )
  if (!confirmed) return

  operationError.value = ''
  busyTemplateId.value = template.id
  try {
    const draft = await emailTemplateService.createVersion(template.id)
    await reloadTemplates()
    editingTemplate.value = draft
    showSuccess(`${draft.code} version ${draft.version} was created as a draft.`)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to create a new email template version.')
  } finally {
    busyTemplateId.value = null
  }
}

async function saveTemplate(values: UpdateEmailTemplateRequest) {
  if (!editingTemplate.value) return
  saving.value = true
  formError.value = ''
  try {
    const updated = await emailTemplateService.update(editingTemplate.value.id, values)
    editingTemplate.value = null
    await reloadTemplates()
    showSuccess(`${updated.code} version ${updated.version} was updated.`)
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the email template draft.')
  } finally {
    saving.value = false
  }
}

async function showEditorPreview(values: PreviewEmailTemplateRequest) {
  if (!editingTemplate.value) return
  previewing.value = true
  formError.value = ''
  try {
    preview.value = await emailTemplateService.preview(values)
    previewVersion.value = editingTemplate.value.version
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to preview the email template.')
  } finally {
    previewing.value = false
  }
}

async function viewTemplate(template: EmailTemplateSummary) {
  operationError.value = ''
  busyTemplateId.value = template.id
  try {
    const details = await emailTemplateService.getById(template.id)
    preview.value = await emailTemplateService.preview({
      subjectTemplate: details.subjectTemplate,
      htmlBodyTemplate: details.htmlBodyTemplate,
      ccRecipients: details.ccRecipients,
      bccRecipients: details.bccRecipients,
    })
    previewVersion.value = details.version
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to preview the email template.')
  } finally {
    busyTemplateId.value = null
  }
}

async function publishTemplate(template: EmailTemplateSummary) {
  const confirmed = window.confirm(
    `Publish ${template.code} version ${template.version}? The current active version will become superseded.`,
  )
  if (!confirmed) return

  operationError.value = ''
  busyTemplateId.value = template.id
  try {
    await emailTemplateService.publish(template.id)
    await reloadTemplates()
    showSuccess(`${template.code} version ${template.version} is now active.`)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to publish the email template.')
  } finally {
    busyTemplateId.value = null
  }
}

async function deleteTemplate(template: EmailTemplateSummary) {
  const confirmed = window.confirm(
    `Delete draft ${template.code} version ${template.version}? This cannot be undone.`,
  )
  if (!confirmed) return

  operationError.value = ''
  busyTemplateId.value = template.id
  try {
    await emailTemplateService.delete(template.id)
    await reloadTemplates()
    showSuccess(`${template.code} version ${template.version} was deleted.`)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to delete the email template draft.')
  } finally {
    busyTemplateId.value = null
  }
}

function statusClass(status: EmailTemplateStatus) {
  if (status === 'Active') return 'is-active'
  if (status === 'Draft') return 'is-draft'
  return 'is-inactive'
}

function formatDate(value: string | null) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('en-MY', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  }).format(new Date(value))
}

function getErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ApiError || error instanceof Error) return error.message
  return fallback
}
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Email administration</p>
        <h1>Email Templates</h1>
        <p class="page-description">
          Version and preview the Purchase Order email used for future supplier messages.
        </p>
      </div>
    </header>

    <div class="summary-grid" aria-label="Email template summary">
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Active versions</span>
        <strong>{{ activeCount }}</strong>
        <span>Used for new Purchase Order emails</span>
      </article>
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Draft versions</span>
        <strong>{{ draftCount }}</strong>
        <span>Available for editing and preview</span>
      </article>
      <article class="summary-card">
        <span class="summary-label">Superseded</span>
        <strong>{{ supersededCount }}</strong>
        <span>Retained for version history</span>
      </article>
    </div>

    <AppToast :toast="toast" @dismiss="dismissToast" />

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">&times;</button>
    </div>

    <section class="data-panel" aria-labelledby="email-template-list-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="email-template-list-title">Purchase Order template versions</h2>
          <p>{{ visibleTemplates.length }} versions shown</p>
        </div>

        <div class="toolbar-actions">
          <label class="search-control">
            <span class="sr-only">Search email templates</span>
            <span aria-hidden="true">⌕</span>
            <input v-model="search" type="search" placeholder="Search code, name, or version" />
          </label>
          <label class="step-filter-control">
            <span class="sr-only">Filter by status</span>
            <select v-model="statusFilter">
              <option value="All">All versions</option>
              <option value="Draft">Draft</option>
              <option value="Active">Active</option>
              <option value="Superseded">Superseded</option>
            </select>
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading email templates</strong>
        <p>Retrieving the Purchase Order template versions.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Email templates could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadTemplates">Try again</button>
      </div>

      <div v-else-if="visibleTemplates.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">ET</div>
        <strong>No email template versions found</strong>
        <p>Apply the latest database migration and restart the API to create the default template.</p>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Template</th>
              <th>Recipient rule</th>
              <th>Version</th>
              <th>Status</th>
              <th>Published</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="template in visibleTemplates" :key="template.id">
              <td>
                <div class="email-template-identity">
                  <span class="code-avatar">ET</span>
                  <span><strong>{{ template.name }}</strong><small>{{ template.code }}</small></span>
                </div>
              </td>
              <td>Supplier Email</td>
              <td>Version {{ template.version }}</td>
              <td>
                <span class="status-badge" :class="statusClass(template.status)">
                  <span aria-hidden="true"></span>{{ template.status }}
                </span>
              </td>
              <td>{{ formatDate(template.publishedAtUtc) }}</td>
              <td>
                <div class="row-actions email-template-row-actions">
                  <button
                    class="text-button"
                    type="button"
                    :disabled="busyTemplateId === template.id"
                    @click="viewTemplate(template)"
                  >
                    Preview
                  </button>
                  <button
                    v-if="template.status === 'Draft'"
                    class="text-button"
                    type="button"
                    :disabled="busyTemplateId === template.id"
                    @click="editTemplate(template)"
                  >
                    Edit
                  </button>
                  <button
                    v-if="template.status === 'Active'"
                    class="text-button"
                    type="button"
                    :disabled="busyTemplateId === template.id"
                    @click="createVersion(template)"
                  >
                    New version
                  </button>
                  <button
                    v-if="template.status === 'Draft'"
                    class="text-button text-button-positive"
                    type="button"
                    :disabled="busyTemplateId === template.id"
                    @click="publishTemplate(template)"
                  >
                    Publish
                  </button>
                  <button
                    v-if="template.status === 'Draft'"
                    class="text-button text-button-danger"
                    type="button"
                    :disabled="busyTemplateId === template.id"
                    @click="deleteTemplate(template)"
                  >
                    Delete
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <EmailTemplateEditor
      v-if="editingTemplate"
      :template="editingTemplate"
      :saving="saving"
      :previewing="previewing"
      :error-message="formError"
      @cancel="editingTemplate = null"
      @save="saveTemplate"
      @preview="showEditorPreview"
    />

    <EmailTemplatePreview
      v-if="preview"
      :preview="preview"
      :version="previewVersion"
      @close="preview = null"
    />
  </section>
</template>
