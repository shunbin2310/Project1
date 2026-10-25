<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import SupplierProductForm from '@/components/supplier-products/SupplierProductForm.vue'
import AppToast from '@/components/ui/AppToast.vue'
import TablePagination from '@/components/ui/TablePagination.vue'
import { usePagination } from '@/composables/usePagination'
import { useToast } from '@/composables/useToast'
import { productService } from '@/services/productService'
import { supplierProductService } from '@/services/supplierProductService'
import { supplierService } from '@/services/supplierService'
import type { Product } from '@/types/product'
import type { Supplier } from '@/types/supplier'
import type { SupplierProduct, SupplierProductFormValues } from '@/types/supplierProduct'

const supplierProducts = ref<SupplierProduct[]>([])
const suppliers = ref<Supplier[]>([])
const products = ref<Product[]>([])
const loading = ref(true)
const saving = ref(false)
const busySupplierProductId = ref<number | null>(null)
const search = ref('')
const supplierFilter = ref('all')
const productFilter = ref('all')
const statusFilter = ref('all')
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const formOpen = ref(false)
const editingSupplierProduct = ref<SupplierProduct | null>(null)
const { toast, showSuccess, dismissToast } = useToast()

const activeCount = computed(
  () => supplierProducts.value.filter((item) => isAvailable(item)).length,
)

const visibleSupplierProducts = computed(() => {
  const term = search.value.trim().toLowerCase()

  return supplierProducts.value.filter((item) => {
    if (supplierFilter.value !== 'all' && item.supplierId !== Number(supplierFilter.value)) {
      return false
    }

    if (productFilter.value !== 'all' && item.productId !== Number(productFilter.value)) {
      return false
    }

    if (statusFilter.value === 'active' && !isAvailable(item)) return false
    if (statusFilter.value === 'inactive' && isAvailable(item)) return false

    if (!term) return true

    return [item.supplierCode, item.supplierName, item.productCode, item.productName].some(
      (value) => value.toLowerCase().includes(term),
    )
  })
})

const {
  page,
  pageSize,
  paginatedItems: paginatedSupplierProducts,
} = usePagination(visibleSupplierProducts, [search, supplierFilter, productFilter, statusFilter])

onMounted(loadData)

async function loadData() {
  loading.value = true
  loadError.value = ''

  try {
    const [relationships, supplierRecords, productRecords] = await Promise.all([
      supplierProductService.getAll({ includeInactive: true }),
      supplierService.getAll(true),
      productService.getAll(true),
    ])

    supplierProducts.value = relationships
    suppliers.value = supplierRecords
    products.value = productRecords
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load supplier-product relationships.')
  } finally {
    loading.value = false
  }
}

function openCreateForm() {
  editingSupplierProduct.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(supplierProduct: SupplierProduct) {
  editingSupplierProduct.value = supplierProduct
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingSupplierProduct.value = null
  formError.value = ''
}

async function saveSupplierProduct(values: SupplierProductFormValues) {
  saving.value = true
  formError.value = ''

  try {
    if (editingSupplierProduct.value) {
      const current = editingSupplierProduct.value
      await supplierProductService.update(current.id, {
        isPreferred: current.isPreferred,
        isActive: values.isActive,
      })
      showSuccess(`${current.supplierCode} and ${current.productCode} were updated.`)
    } else {
      const created = await supplierProductService.create({
        supplierId: values.supplierId,
        productId: values.productId,
        isPreferred: false,
      })
      showSuccess(`${created.supplierCode} is now linked to ${created.productCode}.`)
    }

    formOpen.value = false
    editingSupplierProduct.value = null
    await loadData()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the relationship.')
  } finally {
    saving.value = false
  }
}

async function deleteSupplierProduct(supplierProduct: SupplierProduct) {
  if (busySupplierProductId.value !== null) return
  const confirmed = window.confirm(
    `Delete the relationship between ${supplierProduct.supplierCode} and ${supplierProduct.productCode}? The supplier and product records will remain.`,
  )

  if (!confirmed) return

  busySupplierProductId.value = supplierProduct.id
  operationError.value = ''

  try {
    await supplierProductService.delete(supplierProduct.id)
    showSuccess(
      `${supplierProduct.supplierCode} and ${supplierProduct.productCode} are no longer linked.`,
    )
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to delete the relationship.')
  } finally {
    busySupplierProductId.value = null
  }
}

function isAvailable(supplierProduct: SupplierProduct) {
  const supplier = suppliers.value.find((item) => item.id === supplierProduct.supplierId)
  const product = products.value.find((item) => item.id === supplierProduct.productId)

  return Boolean(supplierProduct.isActive && supplier?.isActive && product?.isActive)
}

function relationshipStatus(supplierProduct: SupplierProduct) {
  if (!supplierProduct.isActive) return 'Inactive'

  const supplier = suppliers.value.find((item) => item.id === supplierProduct.supplierId)
  const product = products.value.find((item) => item.id === supplierProduct.productId)

  return supplier?.isActive && product?.isActive ? 'Active' : 'Unavailable'
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
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Procurement settings</p>
        <h1>Supplier Products</h1>
        <p class="page-description">
          Define which suppliers can provide each catalog product before requesting quotations.
        </p>
      </div>
      <button class="button button-primary" type="button" @click="openCreateForm">
        <span aria-hidden="true">+</span>
        New relationship
      </button>
    </header>

    <div class="summary-grid supplier-product-summary-grid" aria-label="Supplier product summary">
      <article class="summary-card">
        <span class="summary-label">Total relationships</span>
        <strong>{{ supplierProducts.length }}</strong>
        <span>All supplier and product links</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Available</span>
        <strong>{{ activeCount }}</strong>
        <span>Ready for purchasing activities</span>
      </article>
    </div>

    <AppToast :toast="toast" @dismiss="dismissToast" />

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">&times;</button>
    </div>

    <section class="data-panel" aria-labelledby="supplier-product-list-title">
      <div class="panel-toolbar supplier-product-toolbar">
        <div>
          <h2 id="supplier-product-list-title">Supply directory</h2>
          <p>{{ visibleSupplierProducts.length }} relationships shown</p>
        </div>

        <div class="toolbar-actions supplier-product-filters">
          <label class="search-control">
            <span class="sr-only">Search supplier products</span>
            <span aria-hidden="true">⌕</span>
            <input v-model="search" type="search" placeholder="Search supplier or product" />
          </label>

          <label class="relationship-filter-control">
            <span class="sr-only">Filter by supplier</span>
            <select v-model="supplierFilter" aria-label="Filter by supplier">
              <option value="all">All suppliers</option>
              <option v-for="supplier in suppliers" :key="supplier.id" :value="String(supplier.id)">
                {{ supplier.code }} - {{ supplier.name }}
              </option>
            </select>
          </label>

          <label class="relationship-filter-control">
            <span class="sr-only">Filter by product</span>
            <select v-model="productFilter" aria-label="Filter by product">
              <option value="all">All products</option>
              <option v-for="product in products" :key="product.id" :value="String(product.id)">
                {{ product.code }} - {{ product.name }}
              </option>
            </select>
          </label>

          <label class="relationship-filter-control">
            <span class="sr-only">Filter by status</span>
            <select v-model="statusFilter" aria-label="Filter by status">
              <option value="all">All statuses</option>
              <option value="active">Available</option>
              <option value="inactive">Inactive / unavailable</option>
            </select>
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading supplier products</strong>
        <p>Retrieving supply relationships and catalog information.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Supplier products could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button
          class="button button-secondary"
          type="button"
          @click="loadData"
          :disabled="loading"
          :aria-busy="loading"
        >
          Try again
        </button>
      </div>

      <div v-else-if="visibleSupplierProducts.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">SP</div>
        <strong>{{
          supplierProducts.length ? 'No matching relationships' : 'No relationships yet'
        }}</strong>
        <p>
          {{
            supplierProducts.length
              ? 'Change the search or filters to view other supplier products.'
              : 'Link a supplier to a product before preparing supplier quotations.'
          }}
        </p>
        <button
          v-if="supplierProducts.length === 0"
          class="button button-primary"
          type="button"
          @click="openCreateForm"
        >
          Create relationship
        </button>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Supplier</th>
              <th>Product</th>
              <th>Default price</th>
              <th>Status</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="supplierProduct in paginatedSupplierProducts" :key="supplierProduct.id">
              <td>
                <div class="supplier-identity">
                  <span class="code-avatar">{{ supplierProduct.supplierCode.slice(0, 2) }}</span>
                  <span>
                    <strong>{{ supplierProduct.supplierName }}</strong>
                    <small>{{ supplierProduct.supplierCode }}</small>
                  </span>
                </div>
              </td>
              <td>
                <div class="product-identity">
                  <span class="code-avatar">{{ supplierProduct.productCode.slice(0, 2) }}</span>
                  <span>
                    <strong>{{ supplierProduct.productName }}</strong>
                    <small>{{ supplierProduct.productCode }}</small>
                  </span>
                </div>
              </td>
              <td>{{ formatCurrency(supplierProduct.productDefaultUnitPrice) }}</td>
              <td>
                <span
                  class="status-badge"
                  :class="isAvailable(supplierProduct) ? 'is-active' : 'is-inactive'"
                >
                  <span aria-hidden="true"></span>
                  {{ relationshipStatus(supplierProduct) }}
                </span>
              </td>
              <td>
                <div class="row-actions">
                  <button class="text-button" type="button" @click="openEditForm(supplierProduct)">
                    Edit
                  </button>
                  <button
                    class="text-button text-button-danger"
                    type="button"
                    @click="deleteSupplierProduct(supplierProduct)"
                    :disabled="busySupplierProductId !== null"
                    :aria-busy="busySupplierProductId === supplierProduct.id"
                  >
                    Delete
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <TablePagination
        v-if="!loading && !loadError && visibleSupplierProducts.length > 0"
        v-model:page="page"
        v-model:page-size="pageSize"
        :total="visibleSupplierProducts.length"
        label="Supplier products"
        item-label="relationships"
      />
    </section>

    <SupplierProductForm
      v-if="formOpen"
      :supplier-product="editingSupplierProduct"
      :suppliers="suppliers"
      :products="products"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveSupplierProduct"
    />
  </section>
</template>
