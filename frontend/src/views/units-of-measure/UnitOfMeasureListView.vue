<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import UnitOfMeasureForm from '@/components/units-of-measure/UnitOfMeasureForm.vue'
import { unitOfMeasureService } from '@/services/unitOfMeasureService'
import type { UnitOfMeasure, UnitOfMeasureFormValues } from '@/types/unitOfMeasure'

const units = ref<UnitOfMeasure[]>([])
const loading = ref(true)
const saving = ref(false)
const search = ref('')
const includeInactive = ref(false)
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const successMessage = ref('')
const formOpen = ref(false)
const editingUnit = ref<UnitOfMeasure | null>(null)

const activeCount = computed(() => units.value.filter((item) => item.isActive).length)
const inactiveCount = computed(() => units.value.length - activeCount.value)

const visibleUnits = computed(() => {
  const term = search.value.trim().toLowerCase()

  return units.value.filter((unit) => {
    if (!includeInactive.value && !unit.isActive) return false
    if (!term) return true

    return [unit.code, unit.name, unit.description ?? ''].some((value) =>
      value.toLowerCase().includes(term),
    )
  })
})

onMounted(loadUnits)

async function loadUnits() {
  loading.value = true
  loadError.value = ''

  try {
    units.value = await unitOfMeasureService.getAll(true)
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load units of measure.')
  } finally {
    loading.value = false
  }
}

function openCreateForm() {
  editingUnit.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(unit: UnitOfMeasure) {
  editingUnit.value = unit
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingUnit.value = null
  formError.value = ''
}

async function saveUnit(values: UnitOfMeasureFormValues) {
  saving.value = true
  formError.value = ''

  try {
    if (editingUnit.value) {
      await unitOfMeasureService.update(editingUnit.value.id, {
        name: values.name,
        description: values.description,
        isActive: values.isActive,
      })
      showSuccess(`${editingUnit.value.code} was updated successfully.`)
    } else {
      const unit = await unitOfMeasureService.create({
        code: values.code,
        name: values.name,
        description: values.description,
      })
      showSuccess(`${unit.code} was created successfully.`)
    }

    formOpen.value = false
    editingUnit.value = null
    await loadUnits()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the unit of measure.')
  } finally {
    saving.value = false
  }
}

async function deactivateUnit(unit: UnitOfMeasure) {
  const confirmed = window.confirm(
    `Deactivate ${unit.code} - ${unit.name}? The record will remain available in history.`,
  )

  if (!confirmed) return
  operationError.value = ''

  try {
    await unitOfMeasureService.deactivate(unit.id)
    showSuccess(`${unit.code} was deactivated.`)
    await loadUnits()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to deactivate the unit of measure.')
  }
}

async function reactivateUnit(unit: UnitOfMeasure) {
  operationError.value = ''

  try {
    await unitOfMeasureService.update(unit.id, {
      name: unit.name,
      description: unit.description,
      isActive: true,
    })
    showSuccess(`${unit.code} was reactivated.`)
    await loadUnits()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to reactivate the unit of measure.')
  }
}

function showSuccess(message: string) {
  successMessage.value = message
  window.setTimeout(() => {
    if (successMessage.value === message) successMessage.value = ''
  }, 3500)
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback
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
        <p class="eyebrow">Catalog settings</p>
        <h1>Units of Measure</h1>
        <p class="page-description">
          Maintain the standard units used to purchase, receive, and count products.
        </p>
      </div>
      <button class="button button-primary" type="button" @click="openCreateForm">
        <span aria-hidden="true">+</span>
        New unit
      </button>
    </header>

    <div class="summary-grid" aria-label="Unit of measure summary">
      <article class="summary-card">
        <span class="summary-label">Total units</span>
        <strong>{{ units.length }}</strong>
        <span>All unit records</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Active</span>
        <strong>{{ activeCount }}</strong>
        <span>Available for products</span>
      </article>
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Inactive</span>
        <strong>{{ inactiveCount }}</strong>
        <span>Retained for audit history</span>
      </article>
    </div>

    <div v-if="successMessage" class="alert alert-success" role="status">
      <span aria-hidden="true">OK</span>
      {{ successMessage }}
    </div>

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">&times;</button>
    </div>

    <section class="data-panel" aria-labelledby="unit-list-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="unit-list-title">Unit directory</h2>
          <p>{{ visibleUnits.length }} records shown</p>
        </div>

        <div class="toolbar-actions">
          <label class="search-control">
            <span class="sr-only">Search units of measure</span>
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
        <strong>Loading units of measure</strong>
        <p>Retrieving the latest catalog data.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Units of measure could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadUnits">Try again</button>
      </div>

      <div v-else-if="visibleUnits.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">UM</div>
        <strong>{{ search ? 'No matching units' : 'No units of measure yet' }}</strong>
        <p>
          {{
            search
              ? 'Try a different search term or include inactive records.'
              : 'Create the first unit before adding products to the catalog.'
          }}
        </p>
        <button v-if="!search" class="button button-primary" type="button" @click="openCreateForm">
          Create unit of measure
        </button>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Unit</th>
              <th>Description</th>
              <th>Status</th>
              <th>Last updated</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="unit in visibleUnits" :key="unit.id">
              <td>
                <div class="unit-identity">
                  <span class="code-avatar">{{ unit.code.slice(0, 2) }}</span>
                  <span>
                    <strong>{{ unit.name }}</strong>
                    <small>{{ unit.code }}</small>
                  </span>
                </div>
              </td>
              <td class="description-cell">{{ unit.description || 'No description provided' }}</td>
              <td>
                <span class="status-badge" :class="unit.isActive ? 'is-active' : 'is-inactive'">
                  <span aria-hidden="true"></span>
                  {{ unit.isActive ? 'Active' : 'Inactive' }}
                </span>
              </td>
              <td>{{ formatDate(unit.updatedAtUtc ?? unit.createdAtUtc) }}</td>
              <td>
                <div class="row-actions">
                  <button class="text-button" type="button" @click="openEditForm(unit)">
                    Edit
                  </button>
                  <button
                    v-if="unit.isActive"
                    class="text-button text-button-danger"
                    type="button"
                    @click="deactivateUnit(unit)"
                  >
                    Deactivate
                  </button>
                  <button
                    v-else
                    class="text-button text-button-positive"
                    type="button"
                    @click="reactivateUnit(unit)"
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

    <UnitOfMeasureForm
      v-if="formOpen"
      :unit="editingUnit"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveUnit"
    />
  </section>
</template>
