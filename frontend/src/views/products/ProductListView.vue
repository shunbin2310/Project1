<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import ProductForm from '@/components/products/ProductForm.vue'
import { productCategoryService } from '@/services/productCategoryService'
import { productService } from '@/services/productService'
import { unitOfMeasureService } from '@/services/unitOfMeasureService'
import type { ProductCategory } from '@/types/productCategory'
import type { Product, ProductFormValues } from '@/types/product'
import type { UnitOfMeasure } from '@/types/unitOfMeasure'

const products = ref<Product[]>([])
const categories = ref<ProductCategory[]>([])
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
const editingProduct = ref<Product | null>(null)

const activeCount = computed(() => products.value.filter((item) => item.isActive).length)
const inactiveCount = computed(() => products.value.length - activeCount.value)

const visibleProducts = computed(() => {
  const term = search.value.trim().toLowerCase()

  return products.value.filter((product) => {
    if (!includeInactive.value && !product.isActive) return false
    if (!term) return true

    return [
      product.code,
      product.name,
      product.description ?? '',
      product.productCategoryCode,
      product.productCategoryName,
      product.unitOfMeasureCode,
      product.unitOfMeasureName,
    ].some((value) => value.toLowerCase().includes(term))
  })
})

onMounted(loadProducts)

async function loadProducts() {
  loading.value = true
  loadError.value = ''

  try {
    const [productRecords, categoryRecords, unitRecords] = await Promise.all([
      productService.getAll(true),
      productCategoryService.getAll(true),
      unitOfMeasureService.getAll(true),
    ])
    products.value = productRecords
    categories.value = categoryRecords
    units.value = unitRecords
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load the product catalog.')
  } finally {
    loading.value = false
  }
}

function openCreateForm() {
  editingProduct.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(product: Product) {
  editingProduct.value = product
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingProduct.value = null
  formError.value = ''
}

async function saveProduct(values: ProductFormValues) {
  saving.value = true
  formError.value = ''

  try {
    if (editingProduct.value) {
      await productService.update(editingProduct.value.id, values)
      showSuccess(`${editingProduct.value.code} was updated successfully.`)
    } else {
      const product = await productService.create({
        name: values.name,
        description: values.description,
        productCategoryId: values.productCategoryId,
        unitOfMeasureId: values.unitOfMeasureId,
        defaultUnitPrice: values.defaultUnitPrice,
        reorderLevel: values.reorderLevel,
      })
      showSuccess(`${product.code} was created successfully.`)
    }

    formOpen.value = false
    editingProduct.value = null
    await loadProducts()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the product.')
  } finally {
    saving.value = false
  }
}

async function deactivateProduct(product: Product) {
  const confirmed = window.confirm(
    `Deactivate ${product.code} - ${product.name}? The record will remain available in history.`,
  )

  if (!confirmed) return
  operationError.value = ''

  try {
    await productService.deactivate(product.id)
    showSuccess(`${product.code} was deactivated.`)
    await loadProducts()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to deactivate the product.')
  }
}

async function reactivateProduct(product: Product) {
  operationError.value = ''

  try {
    await productService.update(product.id, {
      name: product.name,
      description: product.description,
      productCategoryId: product.productCategoryId,
      unitOfMeasureId: product.unitOfMeasureId,
      defaultUnitPrice: product.defaultUnitPrice,
      reorderLevel: product.reorderLevel,
      isActive: true,
    })
    showSuccess(`${product.code} was reactivated.`)
    await loadProducts()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to reactivate the product.')
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

function formatCurrency(value: number) {
  return new Intl.NumberFormat('en-MY', {
    style: 'currency',
    currency: 'MYR',
    minimumFractionDigits: 2,
  }).format(value)
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('en-MY', { maximumFractionDigits: 3 }).format(value)
}
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Product catalog</p>
        <h1>Products</h1>
        <p class="page-description">
          Maintain purchasable items, standard pricing, units, and replenishment settings.
        </p>
      </div>
      <button class="button button-primary" type="button" @click="openCreateForm">
        <span aria-hidden="true">+</span>
        New product
      </button>
    </header>

    <div class="summary-grid" aria-label="Product summary">
      <article class="summary-card">
        <span class="summary-label">Total products</span>
        <strong>{{ products.length }}</strong>
        <span>All product records</span>
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
      <span aria-hidden="true">OK</span>
      {{ successMessage }}
    </div>

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">&times;</button>
    </div>

    <section class="data-panel" aria-labelledby="product-list-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="product-list-title">Product directory</h2>
          <p>{{ visibleProducts.length }} records shown</p>
        </div>

        <div class="toolbar-actions">
          <label class="search-control">
            <span class="sr-only">Search products</span>
            <span aria-hidden="true">⌕</span>
            <input
              v-model="search"
              type="search"
              placeholder="Search products, categories, or units"
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
        <strong>Loading products</strong>
        <p>Retrieving the latest catalog data.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Products could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadProducts">
          Try again
        </button>
      </div>

      <div v-else-if="visibleProducts.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">PR</div>
        <strong>{{ search ? 'No matching products' : 'No products yet' }}</strong>
        <p>
          {{
            search
              ? 'Try a different search term or include inactive records.'
              : 'Create the first product after preparing categories and units of measure.'
          }}
        </p>
        <button v-if="!search" class="button button-primary" type="button" @click="openCreateForm">
          Create product
        </button>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Product</th>
              <th>Category</th>
              <th>Unit</th>
              <th>Default unit price</th>
              <th>Reorder level</th>
              <th>Status</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="product in visibleProducts" :key="product.id">
              <td>
                <div class="product-identity">
                  <span class="code-avatar">{{ product.code.slice(0, 2) }}</span>
                  <span>
                    <strong>{{ product.name }}</strong>
                    <small>{{ product.code }}</small>
                  </span>
                </div>
              </td>
              <td>
                <div class="contact-stack">
                  <span>{{ product.productCategoryName }}</span>
                  <small>{{ product.productCategoryCode }}</small>
                </div>
              </td>
              <td>
                <div class="contact-stack">
                  <span>{{ product.unitOfMeasureName }}</span>
                  <small>{{ product.unitOfMeasureCode }}</small>
                </div>
              </td>
              <td>{{ formatCurrency(product.defaultUnitPrice) }}</td>
              <td>{{ formatQuantity(product.reorderLevel) }} {{ product.unitOfMeasureCode }}</td>
              <td>
                <span class="status-badge" :class="product.isActive ? 'is-active' : 'is-inactive'">
                  <span aria-hidden="true"></span>
                  {{ product.isActive ? 'Active' : 'Inactive' }}
                </span>
              </td>
              <td>
                <div class="row-actions">
                  <button class="text-button" type="button" @click="openEditForm(product)">
                    Edit
                  </button>
                  <button
                    v-if="product.isActive"
                    class="text-button text-button-danger"
                    type="button"
                    @click="deactivateProduct(product)"
                  >
                    Deactivate
                  </button>
                  <button
                    v-else
                    class="text-button text-button-positive"
                    type="button"
                    @click="reactivateProduct(product)"
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

    <ProductForm
      v-if="formOpen"
      :product="editingProduct"
      :categories="categories"
      :units="units"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveProduct"
    />
  </section>
</template>
