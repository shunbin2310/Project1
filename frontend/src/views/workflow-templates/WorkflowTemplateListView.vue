<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import WorkflowTemplateDetails from '@/components/workflow-templates/WorkflowTemplateDetails.vue'
import WorkflowTemplateForm from '@/components/workflow-templates/WorkflowTemplateForm.vue'
import { userService } from '@/services/userService'
import { ApiError, workflowTemplateService } from '@/services/workflowTemplateService'
import type { User } from '@/types/user'
import type {
  WorkflowTemplate,
  WorkflowTemplateFormValues,
  WorkflowTemplateSummary,
} from '@/types/workflowTemplate'

type TemplateStatusFilter = 'all' | 'draft' | 'active' | 'superseded'

const templates = ref<WorkflowTemplateSummary[]>([])
const users = ref<User[]>([])
const loading = ref(true)
const saving = ref(false)
const busyTemplateId = ref<number | null>(null)
const search = ref('')
const statusFilter = ref<TemplateStatusFilter>('all')
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const successMessage = ref('')
const formOpen = ref(false)
const detailsOpen = ref(false)
const editingTemplate = ref<WorkflowTemplate | null>(null)
const selectedTemplate = ref<WorkflowTemplate | null>(null)

const draftCount = computed(
  () => templates.value.filter((template) => !template.isPublished).length,
)
const activeCount = computed(() => templates.value.filter((template) => template.isActive).length)
const templateCodeCount = computed(
  () => new Set(templates.value.map((template) => template.code)).size,
)
const visibleTemplates = computed(() => {
  const term = search.value.trim().toLowerCase()

  return templates.value.filter((template) => {
    if (statusFilter.value === 'draft' && template.isPublished) return false
    if (statusFilter.value === 'active' && !template.isActive) return false
    if (statusFilter.value === 'superseded' && (!template.isPublished || template.isActive)) {
      return false
    }
    if (!term) return true

    return [template.code, template.name, template.entityType, `version ${template.version}`].some(
      (value) => value.toLowerCase().includes(term),
    )
  })
})

onMounted(loadPage)

async function loadPage() {
  loading.value = true
  loadError.value = ''

  try {
    const [templateRecords, userRecords] = await Promise.all([
      workflowTemplateService.getAll(),
      userService.getAll(true),
    ])
    templates.value = templateRecords
    users.value = userRecords
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load workflow template data.')
  } finally {
    loading.value = false
  }
}

async function reloadTemplates() {
  templates.value = await workflowTemplateService.getAll()
}

function openCreateForm() {
  editingTemplate.value = null
  formError.value = ''
  formOpen.value = true
}

async function openEditForm(template: WorkflowTemplateSummary) {
  if (template.isPublished) return
  formError.value = ''
  operationError.value = ''
  busyTemplateId.value = template.id

  try {
    editingTemplate.value = await workflowTemplateService.getById(template.id)
    formOpen.value = true
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to load the draft for editing.')
  } finally {
    busyTemplateId.value = null
  }
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingTemplate.value = null
  formError.value = ''
}

async function saveTemplate(values: WorkflowTemplateFormValues) {
  saving.value = true
  formError.value = ''

  try {
    if (editingTemplate.value) {
      await workflowTemplateService.update(editingTemplate.value.id, {
        name: values.name,
        steps: values.steps,
      })
      showSuccess(`${values.code} version ${editingTemplate.value.version} was updated.`)
    } else {
      await workflowTemplateService.create(values)
      showSuccess(`${values.code} version 1 was created as a draft.`)
    }

    formOpen.value = false
    editingTemplate.value = null
    await reloadTemplates()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the workflow template.')
  } finally {
    saving.value = false
  }
}

async function openDetails(template: WorkflowTemplateSummary) {
  operationError.value = ''
  busyTemplateId.value = template.id

  try {
    selectedTemplate.value = await workflowTemplateService.getById(template.id)
    detailsOpen.value = true
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to load workflow template details.')
  } finally {
    busyTemplateId.value = null
  }
}

async function createVersion(template: WorkflowTemplateSummary) {
  const confirmed = window.confirm(
    `Create a new draft version from ${template.code} version ${template.version}?`,
  )
  if (!confirmed) return

  operationError.value = ''
  busyTemplateId.value = template.id
  try {
    const draft = await workflowTemplateService.createVersion(template.id)
    await reloadTemplates()
    editingTemplate.value = draft
    formError.value = ''
    formOpen.value = true
    showSuccess(`${draft.code} version ${draft.version} was created as a draft.`)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to create a new workflow version.')
  } finally {
    busyTemplateId.value = null
  }
}

async function publishTemplate(template: WorkflowTemplateSummary) {
  const confirmed = window.confirm(
    `Publish ${template.code} version ${template.version}? It will become active for new records and cannot be edited or deleted afterward.`,
  )
  if (!confirmed) return

  operationError.value = ''
  busyTemplateId.value = template.id
  try {
    await workflowTemplateService.publish(template.id)
    await reloadTemplates()
    showSuccess(`${template.code} version ${template.version} is now active.`)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to publish the workflow template.')
  } finally {
    busyTemplateId.value = null
  }
}

async function deleteTemplate(template: WorkflowTemplateSummary) {
  const confirmed = window.confirm(
    `Delete draft ${template.code} version ${template.version}? This cannot be undone.`,
  )
  if (!confirmed) return

  operationError.value = ''
  busyTemplateId.value = template.id
  try {
    await workflowTemplateService.delete(template.id)
    await reloadTemplates()
    showSuccess(`${template.code} version ${template.version} was deleted.`)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to delete the workflow template.')
  } finally {
    busyTemplateId.value = null
  }
}

function templateStatus(template: WorkflowTemplateSummary) {
  if (!template.isPublished) return 'Draft'
  return template.isActive ? 'Active' : 'Superseded'
}

function statusClass(template: WorkflowTemplateSummary) {
  if (!template.isPublished) return 'is-draft'
  return template.isActive ? 'is-active' : 'is-inactive'
}

function formatDate(value: string | null) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('en-MY', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  }).format(new Date(value))
}

function showSuccess(message: string) {
  successMessage.value = message
  window.setTimeout(() => {
    if (successMessage.value === message) successMessage.value = ''
  }, 3500)
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
        <p class="eyebrow">Workflow administration</p>
        <h1>Workflow Templates</h1>
        <p class="page-description">
          Design approval routes and publish versioned definitions for new business records.
        </p>
      </div>
      <button class="button button-primary" type="button" @click="openCreateForm">
        <span aria-hidden="true">＋</span>
        New template
      </button>
    </header>

    <div class="summary-grid" aria-label="Workflow template summary">
      <article class="summary-card">
        <span class="summary-label">Template codes</span>
        <strong>{{ templateCodeCount }}</strong>
        <span>Independent workflow definitions</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Active versions</span>
        <strong>{{ activeCount }}</strong>
        <span>Used when new records are created</span>
      </article>
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Draft versions</span>
        <strong>{{ draftCount }}</strong>
        <span>Available for editing and validation</span>
      </article>
    </div>

    <Transition name="toast">
      <div v-if="successMessage" class="success-toast" role="status">
        <span class="success-toast-icon" aria-hidden="true">OK</span>
        <div>
          <strong>Operation completed</strong>
          <p>{{ successMessage }}</p>
        </div>
        <button type="button" aria-label="Dismiss success message" @click="successMessage = ''">
          ×
        </button>
      </div>
    </Transition>

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">×</button>
    </div>

    <section class="data-panel" aria-labelledby="workflow-template-list-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="workflow-template-list-title">Template versions</h2>
          <p>{{ visibleTemplates.length }} versions shown</p>
        </div>

        <div class="toolbar-actions">
          <label class="search-control">
            <span class="sr-only">Search workflow templates</span>
            <span aria-hidden="true">⌕</span>
            <input v-model="search" type="search" placeholder="Search code, name, or entity type" />
          </label>
          <label class="step-filter-control">
            <span class="sr-only">Filter workflow templates by status</span>
            <select v-model="statusFilter">
              <option value="all">All versions</option>
              <option value="draft">Draft</option>
              <option value="active">Active</option>
              <option value="superseded">Superseded</option>
            </select>
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading workflow templates</strong>
        <p>Retrieving template versions and workflow definitions.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Workflow templates could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadPage">Try again</button>
      </div>

      <div v-else-if="visibleTemplates.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">WF</div>
        <strong>{{
          search ? 'No matching workflow templates' : 'No versions in this status'
        }}</strong>
        <p>Create a template or choose another search and status filter.</p>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Template</th>
              <th>Entity type</th>
              <th>Version</th>
              <th>Steps</th>
              <th>Status</th>
              <th>Published</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="template in visibleTemplates" :key="template.id">
              <td>
                <div class="workflow-template-identity">
                  <span class="code-avatar">WF</span>
                  <span>
                    <strong>{{ template.name }}</strong>
                    <small>{{ template.code }}</small>
                  </span>
                </div>
              </td>
              <td>
                <span class="table-primary">{{ template.entityType }}</span>
              </td>
              <td>Version {{ template.version }}</td>
              <td>{{ template.stepCount }}</td>
              <td>
                <span class="status-badge" :class="statusClass(template)">
                  <span aria-hidden="true"></span>{{ templateStatus(template) }}
                </span>
              </td>
              <td>{{ formatDate(template.publishedAtUtc) }}</td>
              <td>
                <div class="row-actions workflow-template-row-actions">
                  <button
                    class="text-button"
                    type="button"
                    :disabled="busyTemplateId === template.id"
                    @click="openDetails(template)"
                  >
                    View
                  </button>
                  <button
                    v-if="!template.isPublished"
                    class="text-button"
                    type="button"
                    :disabled="busyTemplateId === template.id"
                    @click="openEditForm(template)"
                  >
                    Edit
                  </button>
                  <button
                    v-if="template.isActive"
                    class="text-button"
                    type="button"
                    :disabled="busyTemplateId === template.id"
                    @click="createVersion(template)"
                  >
                    New version
                  </button>
                  <button
                    v-if="!template.isPublished"
                    class="text-button text-button-positive"
                    type="button"
                    :disabled="busyTemplateId === template.id"
                    @click="publishTemplate(template)"
                  >
                    Publish
                  </button>
                  <button
                    v-if="!template.isPublished"
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

    <WorkflowTemplateForm
      v-if="formOpen"
      :template="editingTemplate"
      :users="users"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveTemplate"
    />

    <WorkflowTemplateDetails
      v-if="detailsOpen && selectedTemplate"
      :template="selectedTemplate"
      :users="users"
      @close="detailsOpen = false"
    />
  </section>
</template>
