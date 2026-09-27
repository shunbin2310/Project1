<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import InventoryHistoryDialog from '@/components/inventory/InventoryHistoryDialog.vue'
import { inventoryService } from '@/services/inventoryService'
import type { InventoryBalance, InventoryStockStatus } from '@/types/inventory'

const statusFilters: { value: '' | InventoryStockStatus; label: string }[] = [
  { value: '', label: 'All stock statuses' },
  { value: 'Healthy', label: 'Healthy' },
  { value: 'LowStock', label: 'Low stock' },
  { value: 'OutOfStock', label: 'Out of stock' },
]

const balances = ref<InventoryBalance[]>([])
const loading = ref(true)
const loadError = ref('')
const search = ref('')
const selectedCategoryId = ref<number | ''>('')
const selectedStatus = ref<'' | InventoryStockStatus>('')
const showInactive = ref(false)
const selectedInventory = ref<InventoryBalance | null>(null)

const activeBalances = computed(() => balances.value.filter((balance) => balance.isProductActive))
const trackedCount = computed(() => activeBalances.value.length)
const lowStockCount = computed(
  () =>
    activeBalances.value.filter(
      (balance) => balance.quantityOnHand > 0 && balance.quantityOnHand <= balance.reorderLevel,
    ).length,
)
const outOfStockCount = computed(
  () => activeBalances.value.filter((balance) => balance.quantityOnHand <= 0).length,
)

const categoryOptions = computed(() => {
  const categories = new Map<number, { id: number; code: string; name: string }>()
  balances.value.forEach((balance) => {
    categories.set(balance.productCategoryId, {
      id: balance.productCategoryId,
      code: balance.productCategoryCode,
      name: balance.productCategoryName,
    })
  })
  return [...categories.values()].sort((left, right) => left.name.localeCompare(right.name))
})

const visibleBalances = computed(() => {
  const term = search.value.trim().toLowerCase()

  return balances.value.filter((balance) => {
    if (!showInactive.value && !balance.isProductActive) return false
    if (selectedCategoryId.value && balance.productCategoryId !== selectedCategoryId.value) {
      return false
    }
    if (selectedStatus.value && stockStatus(balance) !== selectedStatus.value) return false
    if (!term) return true

    return [
      balance.productCode,
      balance.productName,
      balance.productCategoryCode,
      balance.productCategoryName,
      balance.unitOfMeasureCode,
    ].some((value) => value.toLowerCase().includes(term))
  })
})

onMounted(loadInventory)

async function loadInventory() {
  loading.value = true
  loadError.value = ''
  try {
    balances.value = await inventoryService.getAll({ includeInactive: true })
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : 'Unable to load inventory.'
  } finally {
    loading.value = false
  }
}

function stockStatus(balance: InventoryBalance): InventoryStockStatus {
  if (balance.quantityOnHand <= 0) return 'OutOfStock'
  if (balance.quantityOnHand <= balance.reorderLevel) return 'LowStock'
  return 'Healthy'
}

function stockStatusLabel(balance: InventoryBalance) {
  const status = stockStatus(balance)
  if (status === 'OutOfStock') return 'Out of stock'
  if (status === 'LowStock') return 'Low stock'
  return 'Healthy'
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('en-MY', { maximumFractionDigits: 3 }).format(value)
}

function formatDateTime(value: string | null) {
  if (!value) return 'No movement yet'
  return new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Stock control</p>
        <h1>Inventory</h1>
        <p class="page-description">
          Monitor product balances, identify replenishment needs, and review the immutable stock
          movement ledger.
        </p>
      </div>
    </header>

    <div class="summary-grid" aria-label="Inventory summary">
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Tracked products</span>
        <strong>{{ trackedCount }}</strong>
        <span>Active products monitored</span>
      </article>
      <article class="summary-card inventory-summary-low">
        <span class="summary-label">Low stock</span>
        <strong>{{ lowStockCount }}</strong>
        <span>At or below reorder level</span>
      </article>
      <article class="summary-card inventory-summary-out">
        <span class="summary-label">Out of stock</span>
        <strong>{{ outOfStockCount }}</strong>
        <span>No quantity currently available</span>
      </article>
    </div>

    <section class="data-panel" aria-labelledby="inventory-list-title">
      <div class="panel-toolbar inventory-toolbar">
        <div>
          <h2 id="inventory-list-title">Stock balances</h2>
          <p>{{ visibleBalances.length }} products shown</p>
        </div>

        <div class="toolbar-actions inventory-filters">
          <label class="search-control">
            <span class="sr-only">Search inventory</span>
            <span aria-hidden="true">&#8981;</span>
            <input v-model="search" type="search" placeholder="Search product, category, or unit" />
          </label>
          <label class="relationship-filter-control">
            <span class="sr-only">Filter by category</span>
            <select v-model="selectedCategoryId" aria-label="Filter by category">
              <option value="">All categories</option>
              <option v-for="category in categoryOptions" :key="category.id" :value="category.id">
                {{ category.name }} ({{ category.code }})
              </option>
            </select>
          </label>
          <label class="relationship-filter-control">
            <span class="sr-only">Filter by stock status</span>
            <select v-model="selectedStatus" aria-label="Filter by stock status">
              <option v-for="status in statusFilters" :key="status.value" :value="status.value">
                {{ status.label }}
              </option>
            </select>
          </label>
          <label class="filter-control">
            <input v-model="showInactive" type="checkbox" />
            Show inactive
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading inventory</strong>
        <p>Retrieving current product balances.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Inventory could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadInventory">
          Try again
        </button>
      </div>

      <div v-else-if="!visibleBalances.length" class="panel-state">
        <div class="empty-icon" aria-hidden="true">IN</div>
        <strong>{{ balances.length ? 'No matching products' : 'No products to track' }}</strong>
        <p>
          {{
            balances.length
              ? 'Change the search or filters to see more stock balances.'
              : 'Create an active product before monitoring inventory.'
          }}
        </p>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Product</th>
              <th>Category</th>
              <th>On hand</th>
              <th>Reorder level</th>
              <th>Stock status</th>
              <th>Last movement</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="balance in visibleBalances" :key="balance.productId">
              <td>
                <div class="inventory-product-identity">
                  <span class="code-avatar">IN</span>
                  <span>
                    <strong>{{ balance.productName }}</strong>
                    <small>
                      {{ balance.productCode }}
                      <template v-if="!balance.isProductActive"> · Inactive</template>
                    </small>
                  </span>
                </div>
              </td>
              <td>
                <span class="table-primary">{{ balance.productCategoryName }}</span>
                <small class="table-secondary">{{ balance.productCategoryCode }}</small>
              </td>
              <td>
                <strong class="inventory-quantity">{{
                  formatQuantity(balance.quantityOnHand)
                }}</strong>
                <small class="table-secondary">{{ balance.unitOfMeasureCode }}</small>
              </td>
              <td>{{ formatQuantity(balance.reorderLevel) }} {{ balance.unitOfMeasureCode }}</td>
              <td>
                <span
                  class="inventory-stock-status"
                  :class="`status-${stockStatus(balance).toLowerCase()}`"
                >
                  {{ stockStatusLabel(balance) }}
                </span>
              </td>
              <td>{{ formatDateTime(balance.lastUpdatedAtUtc) }}</td>
              <td>
                <div class="row-actions inventory-row-actions">
                  <button class="text-button" type="button" @click="selectedInventory = balance">
                    View history
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <InventoryHistoryDialog
      v-if="selectedInventory"
      :inventory="selectedInventory"
      @close="selectedInventory = null"
    />
  </section>
</template>

<style scoped>
.inventory-summary-low::after {
  background: #f7e8bd;
}

.inventory-summary-out::after {
  background: #f5dddd;
}

.inventory-toolbar {
  align-items: flex-start;
}

.inventory-filters {
  flex-wrap: wrap;
  justify-content: flex-end;
}

.inventory-filters .search-control {
  width: min(280px, 28vw);
}

.inventory-product-identity {
  display: flex;
  min-width: 180px;
  gap: 11px;
  align-items: center;
}

.inventory-product-identity strong,
.inventory-product-identity small {
  display: block;
}

.inventory-product-identity strong {
  color: #203331;
  font-size: 12px;
}

.inventory-product-identity small {
  margin-top: 4px;
  color: #83908e;
  font-size: 9px;
}

.inventory-quantity {
  color: #1a4f46;
  font-size: 14px;
}

.inventory-stock-status {
  display: inline-flex;
  align-items: center;
  padding: 6px 9px;
  font-size: 8px;
  font-weight: 800;
  white-space: nowrap;
  border-radius: 999px;
}

.inventory-stock-status.status-healthy {
  color: #17644f;
  background: #def2ea;
}

.inventory-stock-status.status-lowstock {
  color: #785819;
  background: #fff0ca;
}

.inventory-stock-status.status-outofstock {
  color: #953b3b;
  background: #fbe4e4;
}

.inventory-row-actions {
  min-width: 82px;
}

@media (max-width: 650px) {
  .inventory-filters,
  .inventory-filters .search-control,
  .inventory-filters .relationship-filter-control,
  .inventory-filters .relationship-filter-control select {
    width: 100%;
  }
}
</style>
