<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import { inventoryService } from '@/services/inventoryService'
import type {
  InventoryBalance,
  InventoryTransaction,
  InventoryTransactionType,
} from '@/types/inventory'

const props = defineProps<{
  inventory: InventoryBalance
}>()

const emit = defineEmits<{
  close: []
}>()

const transactionTypes: { value: '' | InventoryTransactionType; label: string }[] = [
  { value: '', label: 'All transaction types' },
  { value: 'GoodsReceipt', label: 'Goods receipt' },
  { value: 'AdjustmentIncrease', label: 'Adjustment increase' },
  { value: 'AdjustmentDecrease', label: 'Adjustment decrease' },
]

const transactions = ref<InventoryTransaction[]>([])
const selectedType = ref<'' | InventoryTransactionType>('')
const dateFrom = ref('')
const dateTo = ref('')
const loading = ref(true)
const loadError = ref('')

const invalidDateRange = computed(() =>
  Boolean(dateFrom.value && dateTo.value && dateFrom.value > dateTo.value),
)

onMounted(loadTransactions)

async function loadTransactions() {
  if (invalidDateRange.value) return

  loading.value = true
  loadError.value = ''
  try {
    transactions.value = await inventoryService.getTransactions(props.inventory.productId, {
      type: selectedType.value || undefined,
      dateFrom: dateFrom.value || undefined,
      dateTo: dateTo.value || undefined,
    })
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : 'Unable to load inventory history.'
  } finally {
    loading.value = false
  }
}

function clearFilters() {
  selectedType.value = ''
  dateFrom.value = ''
  dateTo.value = ''
  void loadTransactions()
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('en-MY', { maximumFractionDigits: 3 }).format(value)
}

function formatQuantityChange(value: number) {
  const formatted = formatQuantity(Math.abs(value))
  return `${value >= 0 ? '+' : '-'}${formatted}`
}

function formatDateTime(value: string | null) {
  if (!value) return 'No stock movement yet'
  return new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function typeLabel(type: InventoryTransactionType) {
  return transactionTypes.find((option) => option.value === type)?.label ?? type
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('close')">
    <section
      class="modal-card inventory-history-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="inventory-history-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Inventory ledger</p>
          <h2 id="inventory-history-title">{{ inventory.productName }}</h2>
          <p class="inventory-history-code">
            {{ inventory.productCode }} · {{ inventory.unitOfMeasureCode }}
          </p>
        </div>
        <button class="icon-button" type="button" aria-label="Close history" @click="emit('close')">
          &times;
        </button>
      </header>

      <div class="inventory-history-content">
        <section class="inventory-balance-summary" aria-label="Current inventory balance">
          <article>
            <span>On hand</span>
            <strong>{{ formatQuantity(inventory.quantityOnHand) }}</strong>
            <small>{{ inventory.unitOfMeasureCode }}</small>
          </article>
          <article>
            <span>Reorder level</span>
            <strong>{{ formatQuantity(inventory.reorderLevel) }}</strong>
            <small>{{ inventory.unitOfMeasureCode }}</small>
          </article>
          <article>
            <span>Last movement</span>
            <strong class="inventory-date-value">{{
              formatDateTime(inventory.lastUpdatedAtUtc)
            }}</strong>
          </article>
        </section>

        <section class="details-section inventory-ledger-section">
          <div class="details-section-heading inventory-history-heading">
            <div>
              <h3>Stock movements</h3>
              <p>Immutable quantity changes recorded for this product</p>
            </div>
          </div>

          <form class="inventory-history-filters" @submit.prevent="loadTransactions">
            <label>
              <span>Transaction type</span>
              <select v-model="selectedType" aria-label="Transaction type">
                <option v-for="type in transactionTypes" :key="type.value" :value="type.value">
                  {{ type.label }}
                </option>
              </select>
            </label>
            <label>
              <span>Date from</span>
              <input v-model="dateFrom" type="date" aria-label="Date from" />
            </label>
            <label>
              <span>Date to</span>
              <input v-model="dateTo" type="date" aria-label="Date to" />
            </label>
            <button
              class="button button-primary"
              type="submit"
              :disabled="loading || invalidDateRange"
            >
              Apply filters
            </button>
            <button
              class="button button-secondary"
              type="button"
              :disabled="loading"
              @click="clearFilters"
            >
              Clear
            </button>
          </form>
          <p v-if="invalidDateRange" class="field-error" role="alert">
            Date to must be on or after date from.
          </p>

          <div v-if="loading" class="panel-state inventory-history-state" aria-live="polite">
            <span class="spinner" aria-hidden="true"></span>
            <strong>Loading stock movements</strong>
          </div>
          <div v-else-if="loadError" class="panel-state panel-state-error inventory-history-state">
            <strong>Inventory history could not be loaded</strong>
            <p>{{ loadError }}</p>
            <button class="button button-secondary" type="button" @click="loadTransactions">
              Try again
            </button>
          </div>
          <div v-else-if="!transactions.length" class="inventory-empty-history">
            <strong>No stock movements found</strong>
            <p>This product has no transactions matching the selected filters.</p>
          </div>
          <div v-else class="details-table table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Type</th>
                  <th>Reference</th>
                  <th>Before</th>
                  <th>Change</th>
                  <th>After</th>
                  <th>Performed by</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="transaction in transactions" :key="transaction.id">
                  <td>{{ formatDateTime(transaction.occurredAtUtc) }}</td>
                  <td>{{ typeLabel(transaction.type) }}</td>
                  <td>
                    <strong>{{ transaction.referenceNumber }}</strong>
                    <small>{{ transaction.referenceType }}</small>
                  </td>
                  <td>{{ formatQuantity(transaction.quantityBefore) }}</td>
                  <td>
                    <strong
                      class="quantity-change"
                      :class="transaction.quantityChange >= 0 ? 'is-increase' : 'is-decrease'"
                    >
                      {{ formatQuantityChange(transaction.quantityChange) }}
                    </strong>
                  </td>
                  <td>{{ formatQuantity(transaction.quantityAfter) }}</td>
                  <td>{{ transaction.performedByName }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <footer class="modal-actions">
          <button class="button button-secondary" type="button" @click="emit('close')">
            Close
          </button>
        </footer>
      </div>
    </section>
  </div>
</template>

<style scoped>
.inventory-history-modal {
  width: min(1120px, 100%);
}

.inventory-history-code {
  margin: 6px 0 0;
  color: #7c8987;
  font-size: 10px;
  font-weight: 700;
}

.inventory-history-content {
  display: grid;
  gap: 18px;
  padding: 23px 25px 25px;
}

.inventory-balance-summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  overflow: hidden;
  background: linear-gradient(135deg, #edf7f3, #f8faf9);
  border: 1px solid #d4e4df;
  border-radius: 13px;
}

.inventory-balance-summary article {
  min-width: 0;
  padding: 18px 20px;
}

.inventory-balance-summary article + article {
  border-left: 1px solid #d7e5e0;
}

.inventory-balance-summary span,
.inventory-balance-summary strong,
.inventory-balance-summary small {
  display: block;
}

.inventory-balance-summary span {
  color: #788783;
  font-size: 8px;
  font-weight: 800;
  letter-spacing: 0.07em;
  text-transform: uppercase;
}

.inventory-balance-summary strong {
  margin-top: 7px;
  color: #1c3b36;
  font-size: 20px;
}

.inventory-balance-summary small {
  margin-top: 3px;
  color: #70807d;
  font-size: 9px;
}

.inventory-balance-summary .inventory-date-value {
  font-size: 12px;
  line-height: 1.5;
}

.inventory-ledger-section {
  overflow: hidden;
}

.inventory-history-heading {
  padding: 17px 18px;
  border-bottom: 1px solid #e0e8e5;
}

.inventory-history-filters {
  display: grid;
  grid-template-columns: minmax(170px, 1fr) repeat(2, minmax(145px, 0.75fr)) auto auto;
  gap: 10px;
  align-items: end;
  padding: 14px 18px;
  background: #f8faf9;
  border-bottom: 1px solid #e0e8e5;
}

.inventory-history-filters label > span {
  display: block;
  margin-bottom: 6px;
  color: #596b68;
  font-size: 9px;
  font-weight: 750;
}

.inventory-history-filters select,
.inventory-history-filters input {
  width: 100%;
  min-height: 40px;
  padding: 0 10px;
  color: #213b38;
  background: #fff;
  border: 1px solid #d3dfdc;
  border-radius: 9px;
}

.field-error {
  margin: 10px 18px;
  color: #a33f3f;
  font-size: 10px;
}

.inventory-history-state {
  min-height: 210px;
}

.inventory-empty-history {
  padding: 48px 20px;
  text-align: center;
}

.inventory-empty-history strong {
  color: #29413e;
  font-size: 13px;
}

.inventory-empty-history p {
  margin: 7px 0 0;
  color: #7b8987;
  font-size: 10px;
}

.details-table td strong,
.details-table td small {
  display: block;
}

.details-table td small {
  margin-top: 4px;
  color: #82908d;
  font-size: 8px;
}

.quantity-change.is-increase {
  color: #17705c;
}

.quantity-change.is-decrease {
  color: #a33f3f;
}

@media (max-width: 900px) {
  .inventory-history-filters {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 650px) {
  .inventory-balance-summary,
  .inventory-history-filters {
    grid-template-columns: 1fr;
  }

  .inventory-balance-summary article + article {
    border-top: 1px solid #d7e5e0;
    border-left: 0;
  }
}
</style>
