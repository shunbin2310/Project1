<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import DepartmentForm from '@/components/departments/DepartmentForm.vue'
import { ApiError, departmentService } from '@/services/departmentService'
import type { Department, DepartmentFormValues } from '@/types/department'

const departments = ref<Department[]>([])
const loading = ref(true)
const saving = ref(false)
const search = ref('')
const includeInactive = ref(false)
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const successMessage = ref('')
const formOpen = ref(false)
const editingDepartment = ref<Department | null>(null)

const activeCount = computed(() => departments.value.filter((item) => item.isActive).length)
const inactiveCount = computed(() => departments.value.length - activeCount.value)

const visibleDepartments = computed(() => {
  const term = search.value.trim().toLowerCase()

  return departments.value.filter((department) => {
    if (!includeInactive.value && !department.isActive) return false
    if (!term) return true

    return [department.code, department.name, department.description ?? ''].some((value) =>
      value.toLowerCase().includes(term),
    )
  })
})

onMounted(loadDepartments)

async function loadDepartments() {
  loading.value = true
  loadError.value = ''

  try {
    departments.value = await departmentService.getAll(true)
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load departments.')
  } finally {
    loading.value = false
  }
}

function openCreateForm() {
  editingDepartment.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(department: Department) {
  editingDepartment.value = department
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingDepartment.value = null
  formError.value = ''
}

async function saveDepartment(values: DepartmentFormValues) {
  saving.value = true
  formError.value = ''

  try {
    if (editingDepartment.value) {
      await departmentService.update(editingDepartment.value.id, {
        name: values.name,
        description: values.description,
        isActive: values.isActive,
      })
      showSuccess(`${values.code} was updated successfully.`)
    } else {
      await departmentService.create({
        code: values.code,
        name: values.name,
        description: values.description,
      })
      showSuccess(`${values.code} was created successfully.`)
    }

    formOpen.value = false
    editingDepartment.value = null
    await loadDepartments()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the department.')
  } finally {
    saving.value = false
  }
}

async function deactivateDepartment(department: Department) {
  const confirmed = window.confirm(
    `Deactivate ${department.code} — ${department.name}? The record will remain available in history.`,
  )

  if (!confirmed) return

  operationError.value = ''

  try {
    await departmentService.deactivate(department.id)
    showSuccess(`${department.code} was deactivated.`)
    await loadDepartments()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to deactivate the department.')
  }
}

async function reactivateDepartment(department: Department) {
  operationError.value = ''

  try {
    await departmentService.update(department.id, {
      name: department.name,
      description: department.description,
      isActive: true,
    })
    showSuccess(`${department.code} was reactivated.`)
    await loadDepartments()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to reactivate the department.')
  }
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

function formatDate(value: string | null) {
  if (!value) return 'Never'

  return new Intl.DateTimeFormat('en-MY', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  }).format(new Date(value))
}
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Organization settings</p>
        <h1>Departments</h1>
        <p class="page-description">
          Maintain the teams that own purchase requests, approvals, and inventory activities.
        </p>
      </div>
      <button class="button button-primary" type="button" @click="openCreateForm">
        <span aria-hidden="true">＋</span>
        New department
      </button>
    </header>

    <div class="summary-grid" aria-label="Department summary">
      <article class="summary-card">
        <span class="summary-label">Total departments</span>
        <strong>{{ departments.length }}</strong>
        <span>All department records</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Active</span>
        <strong>{{ activeCount }}</strong>
        <span>Available for new requests</span>
      </article>
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Inactive</span>
        <strong>{{ inactiveCount }}</strong>
        <span>Retained for audit history</span>
      </article>
    </div>

    <div v-if="successMessage" class="alert alert-success" role="status">
      <span aria-hidden="true">✓</span>
      {{ successMessage }}
    </div>

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">×</button>
    </div>

    <section class="data-panel" aria-labelledby="department-list-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="department-list-title">Department directory</h2>
          <p>{{ visibleDepartments.length }} records shown</p>
        </div>

        <div class="toolbar-actions">
          <label class="search-control">
            <span class="sr-only">Search departments</span>
            <span aria-hidden="true">⌕</span>
            <input v-model="search" type="search" placeholder="Search code, name, or description" />
          </label>

          <label class="filter-control">
            <input v-model="includeInactive" type="checkbox" />
            Show inactive
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading departments</strong>
        <p>Retrieving the latest organization data.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Departments could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadDepartments">
          Try again
        </button>
      </div>

      <div v-else-if="visibleDepartments.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">DP</div>
        <strong>{{ search ? 'No matching departments' : 'No departments yet' }}</strong>
        <p>
          {{
            search
              ? 'Try a different search term or include inactive records.'
              : 'Create the first department to begin organizing purchase requests.'
          }}
        </p>
        <button v-if="!search" class="button button-primary" type="button" @click="openCreateForm">
          Create department
        </button>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Department</th>
              <th>Description</th>
              <th>Status</th>
              <th>Last updated</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="department in visibleDepartments" :key="department.id">
              <td>
                <div class="department-identity">
                  <span class="code-avatar">{{ department.code.slice(0, 2) }}</span>
                  <span>
                    <strong>{{ department.name }}</strong>
                    <small>{{ department.code }}</small>
                  </span>
                </div>
              </td>
              <td class="description-cell">
                {{ department.description || 'No description provided' }}
              </td>
              <td>
                <span
                  class="status-badge"
                  :class="department.isActive ? 'is-active' : 'is-inactive'"
                >
                  <span aria-hidden="true"></span>
                  {{ department.isActive ? 'Active' : 'Inactive' }}
                </span>
              </td>
              <td>{{ formatDate(department.updatedAtUtc ?? department.createdAtUtc) }}</td>
              <td>
                <div class="row-actions">
                  <button class="text-button" type="button" @click="openEditForm(department)">
                    Edit
                  </button>
                  <button
                    v-if="department.isActive"
                    class="text-button text-button-danger"
                    type="button"
                    @click="deactivateDepartment(department)"
                  >
                    Deactivate
                  </button>
                  <button
                    v-else
                    class="text-button text-button-positive"
                    type="button"
                    @click="reactivateDepartment(department)"
                  >
                    Reactivate
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <DepartmentForm
      v-if="formOpen"
      :department="editingDepartment"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveDepartment"
    />
  </section>
</template>
