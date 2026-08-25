<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import SupplierForm from '@/components/suppliers/SupplierForm.vue'
import { supplierService } from '@/services/supplierService'
import type { Supplier, SupplierFormValues } from '@/types/supplier'

const suppliers = ref<Supplier[]>([])
const loading = ref(true)
const saving = ref(false)
const search = ref('')
const includeInactive = ref(false)
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const successMessage = ref('')
const formOpen = ref(false)
const editingSupplier = ref<Supplier | null>(null)

const activeCount = computed(() => suppliers.value.filter((item) => item.isActive).length)
const inactiveCount = computed(() => suppliers.value.length - activeCount.value)

const visibleSuppliers = computed(() => {
  const term = search.value.trim().toLowerCase()

  return suppliers.value.filter((supplier) => {
    if (!includeInactive.value && !supplier.isActive) return false
    if (!term) return true

    return [
      supplier.code,
      supplier.name,
      supplier.contactPerson ?? '',
      supplier.email ?? '',
      supplier.phone ?? '',
    ].some((value) => value.toLowerCase().includes(term))
  })
})

onMounted(loadSuppliers)

async function loadSuppliers() {
  loading.value = true
  loadError.value = ''

  try {
    suppliers.value = await supplierService.getAll(true)
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load suppliers.')
  } finally {
    loading.value = false
  }
}

function openCreateForm() {
  editingSupplier.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(supplier: Supplier) {
  editingSupplier.value = supplier
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingSupplier.value = null
  formError.value = ''
}

async function saveSupplier(values: SupplierFormValues) {
  saving.value = true
  formError.value = ''

  try {
    if (editingSupplier.value) {
      await supplierService.update(editingSupplier.value.id, {
        name: values.name,
        contactPerson: values.contactPerson,
        email: values.email,
        phone: values.phone,
        address: values.address,
        isActive: values.isActive,
      })
      showSuccess(`${editingSupplier.value.code} was updated successfully.`)
    } else {
      const supplier = await supplierService.create({
        name: values.name,
        contactPerson: values.contactPerson,
        email: values.email,
        phone: values.phone,
        address: values.address,
      })
      showSuccess(`${supplier.code} was created successfully.`)
    }

    formOpen.value = false
    editingSupplier.value = null
    await loadSuppliers()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the supplier.')
  } finally {
    saving.value = false
  }
}

async function deactivateSupplier(supplier: Supplier) {
  const confirmed = window.confirm(
    `Deactivate ${supplier.code} — ${supplier.name}? The record will remain available in history.`,
  )

  if (!confirmed) return

  operationError.value = ''

  try {
    await supplierService.deactivate(supplier.id)
    showSuccess(`${supplier.code} was deactivated.`)
    await loadSuppliers()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to deactivate the supplier.')
  }
}

async function reactivateSupplier(supplier: Supplier) {
  operationError.value = ''

  try {
    await supplierService.update(supplier.id, {
      name: supplier.name,
      contactPerson: supplier.contactPerson,
      email: supplier.email,
      phone: supplier.phone,
      address: supplier.address,
      isActive: true,
    })
    showSuccess(`${supplier.code} was reactivated.`)
    await loadSuppliers()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to reactivate the supplier.')
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
        <p class="eyebrow">Procurement settings</p>
        <h1>Suppliers</h1>
        <p class="page-description">
          Maintain the vendors available for purchase requests, orders, and receiving activities.
        </p>
      </div>
      <button class="button button-primary" type="button" @click="openCreateForm">
        <span aria-hidden="true">＋</span>
        New supplier
      </button>
    </header>

    <div class="summary-grid" aria-label="Supplier summary">
      <article class="summary-card">
        <span class="summary-label">Total suppliers</span>
        <strong>{{ suppliers.length }}</strong>
        <span>All supplier records</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Active</span>
        <strong>{{ activeCount }}</strong>
        <span>Available for purchasing</span>
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

    <section class="data-panel" aria-labelledby="supplier-list-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="supplier-list-title">Supplier directory</h2>
          <p>{{ visibleSuppliers.length }} records shown</p>
        </div>

        <div class="toolbar-actions">
          <label class="search-control">
            <span class="sr-only">Search suppliers</span>
            <span aria-hidden="true">⌕</span>
            <input
              v-model="search"
              type="search"
              placeholder="Search code, name, contact, email, or phone"
            />
          </label>

          <label class="filter-control">
            <input v-model="includeInactive" type="checkbox" />
            Show inactive
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading suppliers</strong>
        <p>Retrieving the latest procurement data.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Suppliers could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadSuppliers">
          Try again
        </button>
      </div>

      <div v-else-if="visibleSuppliers.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">SP</div>
        <strong>{{ search ? 'No matching suppliers' : 'No suppliers yet' }}</strong>
        <p>
          {{
            search
              ? 'Try a different search term or include inactive records.'
              : 'Create the first supplier to begin preparing purchase requests.'
          }}
        </p>
        <button v-if="!search" class="button button-primary" type="button" @click="openCreateForm">
          Create supplier
        </button>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Supplier</th>
              <th>Contact person</th>
              <th>Email / Phone</th>
              <th>Status</th>
              <th>Last updated</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="supplier in visibleSuppliers" :key="supplier.id">
              <td>
                <div class="supplier-identity">
                  <span class="code-avatar">{{ supplier.code.slice(0, 2) }}</span>
                  <span>
                    <strong>{{ supplier.name }}</strong>
                    <small>{{ supplier.code }}</small>
                  </span>
                </div>
              </td>
              <td>{{ supplier.contactPerson || 'Not provided' }}</td>
              <td>
                <div class="contact-stack">
                  <span>{{ supplier.email || 'No email' }}</span>
                  <small>{{ supplier.phone || 'No phone' }}</small>
                </div>
              </td>
              <td>
                <span class="status-badge" :class="supplier.isActive ? 'is-active' : 'is-inactive'">
                  <span aria-hidden="true"></span>
                  {{ supplier.isActive ? 'Active' : 'Inactive' }}
                </span>
              </td>
              <td>{{ formatDate(supplier.updatedAtUtc ?? supplier.createdAtUtc) }}</td>
              <td>
                <div class="row-actions">
                  <button class="text-button" type="button" @click="openEditForm(supplier)">
                    Edit
                  </button>
                  <button
                    v-if="supplier.isActive"
                    class="text-button text-button-danger"
                    type="button"
                    @click="deactivateSupplier(supplier)"
                  >
                    Deactivate
                  </button>
                  <button
                    v-else
                    class="text-button text-button-positive"
                    type="button"
                    @click="reactivateSupplier(supplier)"
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

    <SupplierForm
      v-if="formOpen"
      :supplier="editingSupplier"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveSupplier"
    />
  </section>
</template>
