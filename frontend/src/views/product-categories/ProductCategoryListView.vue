<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import ProductCategoryForm from '@/components/product-categories/ProductCategoryForm.vue'
import { productCategoryService } from '@/services/productCategoryService'
import type { ProductCategory, ProductCategoryFormValues } from '@/types/productCategory'

const categories = ref<ProductCategory[]>([])
const loading = ref(true)
const saving = ref(false)
const search = ref('')
const includeInactive = ref(false)
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const successMessage = ref('')
const formOpen = ref(false)
const editingCategory = ref<ProductCategory | null>(null)

const activeCount = computed(() => categories.value.filter((item) => item.isActive).length)
const inactiveCount = computed(() => categories.value.length - activeCount.value)

const visibleCategories = computed(() => {
  const term = search.value.trim().toLowerCase()

  return categories.value.filter((category) => {
    if (!includeInactive.value && !category.isActive) return false
    if (!term) return true

    return [category.code, category.name, category.description ?? ''].some((value) =>
      value.toLowerCase().includes(term),
    )
  })
})

onMounted(loadCategories)

async function loadCategories() {
  loading.value = true
  loadError.value = ''

  try {
    categories.value = await productCategoryService.getAll(true)
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load product categories.')
  } finally {
    loading.value = false
  }
}

function openCreateForm() {
  editingCategory.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(category: ProductCategory) {
  editingCategory.value = category
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingCategory.value = null
  formError.value = ''
}

async function saveCategory(values: ProductCategoryFormValues) {
  saving.value = true
  formError.value = ''

  try {
    if (editingCategory.value) {
      await productCategoryService.update(editingCategory.value.id, values)
      showSuccess(`${editingCategory.value.code} was updated successfully.`)
    } else {
      const category = await productCategoryService.create({
        name: values.name,
        description: values.description,
      })
      showSuccess(`${category.code} was created successfully.`)
    }

    formOpen.value = false
    editingCategory.value = null
    await loadCategories()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the product category.')
  } finally {
    saving.value = false
  }
}

async function deactivateCategory(category: ProductCategory) {
  const confirmed = window.confirm(
    `Deactivate ${category.code} - ${category.name}? The record will remain available in history.`,
  )

  if (!confirmed) return
  operationError.value = ''

  try {
    await productCategoryService.deactivate(category.id)
    showSuccess(`${category.code} was deactivated.`)
    await loadCategories()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to deactivate the product category.')
  }
}

async function reactivateCategory(category: ProductCategory) {
  operationError.value = ''

  try {
    await productCategoryService.update(category.id, {
      name: category.name,
      description: category.description,
      isActive: true,
    })
    showSuccess(`${category.code} was reactivated.`)
    await loadCategories()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to reactivate the product category.')
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
        <h1>Product Categories</h1>
        <p class="page-description">
          Organize products into consistent categories for purchasing and inventory reporting.
        </p>
      </div>
      <button class="button button-primary" type="button" @click="openCreateForm">
        <span aria-hidden="true">+</span>
        New category
      </button>
    </header>

    <div class="summary-grid" aria-label="Product category summary">
      <article class="summary-card">
        <span class="summary-label">Total categories</span>
        <strong>{{ categories.length }}</strong>
        <span>All category records</span>
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

    <section class="data-panel" aria-labelledby="product-category-list-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="product-category-list-title">Category directory</h2>
          <p>{{ visibleCategories.length }} records shown</p>
        </div>

        <div class="toolbar-actions">
          <label class="search-control">
            <span class="sr-only">Search product categories</span>
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
        <strong>Loading product categories</strong>
        <p>Retrieving the latest catalog data.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Product categories could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadCategories">
          Try again
        </button>
      </div>

      <div v-else-if="visibleCategories.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">PC</div>
        <strong>{{ search ? 'No matching categories' : 'No product categories yet' }}</strong>
        <p>
          {{
            search
              ? 'Try a different search term or include inactive records.'
              : 'Create the first category to begin organizing the product catalog.'
          }}
        </p>
        <button v-if="!search" class="button button-primary" type="button" @click="openCreateForm">
          Create product category
        </button>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Category</th>
              <th>Description</th>
              <th>Status</th>
              <th>Last updated</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="category in visibleCategories" :key="category.id">
              <td>
                <div class="category-identity">
                  <span class="code-avatar">{{ category.code.slice(0, 2) }}</span>
                  <span>
                    <strong>{{ category.name }}</strong>
                    <small>{{ category.code }}</small>
                  </span>
                </div>
              </td>
              <td class="description-cell">
                {{ category.description || 'No description provided' }}
              </td>
              <td>
                <span class="status-badge" :class="category.isActive ? 'is-active' : 'is-inactive'">
                  <span aria-hidden="true"></span>
                  {{ category.isActive ? 'Active' : 'Inactive' }}
                </span>
              </td>
              <td>{{ formatDate(category.updatedAtUtc ?? category.createdAtUtc) }}</td>
              <td>
                <div class="row-actions">
                  <button class="text-button" type="button" @click="openEditForm(category)">
                    Edit
                  </button>
                  <button
                    v-if="category.isActive"
                    class="text-button text-button-danger"
                    type="button"
                    @click="deactivateCategory(category)"
                  >
                    Deactivate
                  </button>
                  <button
                    v-else
                    class="text-button text-button-positive"
                    type="button"
                    @click="reactivateCategory(category)"
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

    <ProductCategoryForm
      v-if="formOpen"
      :category="editingCategory"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveCategory"
    />
  </section>
</template>
