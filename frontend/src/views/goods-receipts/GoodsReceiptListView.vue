<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import GoodsReceiptDetails from '@/components/goods-receipts/GoodsReceiptDetails.vue'
import GoodsReceiptForm from '@/components/goods-receipts/GoodsReceiptForm.vue'
import AppToast from '@/components/ui/AppToast.vue'
import { useToast } from '@/composables/useToast'
import { goodsReceiptService } from '@/services/goodsReceiptService'
import { purchaseOrderService } from '@/services/purchaseOrderService'
import { useAuthStore } from '@/stores/auth'
import { applicationRoles } from '@/types/auth'
import type {
  GoodsReceipt,
  GoodsReceiptFormValues,
  GoodsReceiptStatus,
  ReceivablePurchaseOrder,
} from '@/types/goodsReceipt'
import type { PurchaseOrder } from '@/types/purchaseOrder'

const statusFilters: { value: '' | GoodsReceiptStatus; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Posted', label: 'Posted' },
]

const authStore = useAuthStore()
const canManageGoodsReceipts = computed(
  () =>
    authStore.roles.includes(applicationRoles.admin) ||
    authStore.roles.includes(applicationRoles.warehouseOfficer),
)
const goodsReceipts = ref<GoodsReceipt[]>([])
const purchaseOrders = ref<PurchaseOrder[]>([])
const loading = ref(true)
const saving = ref(false)
const busyReceiptId = ref<number | null>(null)
const search = ref('')
const selectedStatus = ref<'' | GoodsReceiptStatus>('')
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const formOpen = ref(false)
const editingReceipt = ref<GoodsReceipt | null>(null)
const detailsReceipt = ref<GoodsReceipt | null>(null)
const { toast, showSuccess, dismissToast } = useToast()

const draftCount = computed(
  () => goodsReceipts.value.filter((receipt) => receipt.status === 'Draft').length,
)
const postedCount = computed(
  () => goodsReceipts.value.filter((receipt) => receipt.status === 'Posted').length,
)
const openOrderCount = computed(
  () =>
    purchaseOrders.value.filter(
      (order) => order.status === 'Issued' || order.status === 'PartiallyReceived',
    ).length,
)

const receivablePurchaseOrders = computed<ReceivablePurchaseOrder[]>(() => {
  const postedReceipts = goodsReceipts.value.filter((receipt) => receipt.status === 'Posted')

  return purchaseOrders.value
    .filter((order) => order.status === 'Issued' || order.status === 'PartiallyReceived')
    .map((order) => ({
      ...order,
      items: order.items.map((item) => {
        const previouslyReceivedQuantity = postedReceipts
          .filter((receipt) => receipt.purchaseOrderId === order.id)
          .flatMap((receipt) => receipt.items)
          .filter((receiptItem) => receiptItem.purchaseOrderItemId === item.id)
          .reduce((total, receiptItem) => total + receiptItem.quantityReceived, 0)

        return {
          ...item,
          previouslyReceivedQuantity,
          remainingQuantity: Math.max(item.quantity - previouslyReceivedQuantity, 0),
        }
      }),
    }))
    .filter((order) => order.items.some((item) => item.remainingQuantity > 0))
})

const eligiblePurchaseOrders = computed(() => {
  const orderIdsWithDraft = new Set(
    goodsReceipts.value
      .filter((receipt) => receipt.status === 'Draft')
      .map((receipt) => receipt.purchaseOrderId),
  )
  return receivablePurchaseOrders.value.filter((order) => !orderIdsWithDraft.has(order.id))
})

const formPurchaseOrders = computed(() => {
  if (!editingReceipt.value) return eligiblePurchaseOrders.value
  const current = receivablePurchaseOrders.value.find(
    (order) => order.id === editingReceipt.value?.purchaseOrderId,
  )
  return current ? [current] : []
})

const visibleReceipts = computed(() => {
  const term = search.value.trim().toLowerCase()

  return goodsReceipts.value.filter((receipt) => {
    if (selectedStatus.value && receipt.status !== selectedStatus.value) return false
    if (!term) return true

    return [
      receipt.goodsReceiptNumber,
      receipt.purchaseOrderNumber,
      receipt.supplierCode,
      receipt.supplierName,
      receipt.supplierDeliveryNoteNumber ?? '',
    ].some((value) => value.toLowerCase().includes(term))
  })
})

onMounted(loadData)

async function loadData() {
  loading.value = true
  loadError.value = ''

  try {
    const [receiptRecords, orderRecords] = await Promise.all([
      goodsReceiptService.getAll(),
      purchaseOrderService.getAll(),
    ])
    goodsReceipts.value = receiptRecords
    purchaseOrders.value = orderRecords
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load goods receipts.')
  } finally {
    loading.value = false
  }
}

function openCreateForm() {
  editingReceipt.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(receipt: GoodsReceipt) {
  editingReceipt.value = receipt
  detailsReceipt.value = null
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingReceipt.value = null
  formError.value = ''
}

async function saveReceipt(values: GoodsReceiptFormValues) {
  saving.value = true
  formError.value = ''

  try {
    let saved: GoodsReceipt
    if (editingReceipt.value) {
      saved = await goodsReceiptService.update(editingReceipt.value.id, {
        supplierDeliveryNoteNumber: values.supplierDeliveryNoteNumber,
        receivedDate: values.receivedDate,
        notes: values.notes,
        items: values.items,
      })
    } else {
      saved = await goodsReceiptService.create(values)
    }

    formOpen.value = false
    editingReceipt.value = null
    showSuccess(
      `${saved.goodsReceiptNumber} was ${saved.updatedAtUtc ? 'updated' : 'created as a draft'}.`,
    )
    await loadData()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the goods receipt.')
  } finally {
    saving.value = false
  }
}

async function postReceipt(receipt: GoodsReceipt) {
  const confirmed = window.confirm(
    `Post ${receipt.goodsReceiptNumber}? Posted receipts cannot be edited or deleted.`,
  )
  if (!confirmed) return

  busyReceiptId.value = receipt.id
  operationError.value = ''
  try {
    await goodsReceiptService.post(receipt.id)
    showSuccess(`${receipt.goodsReceiptNumber} was posted and the purchase order was updated.`)
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to post the goods receipt.')
  } finally {
    busyReceiptId.value = null
  }
}

async function deleteReceipt(receipt: GoodsReceipt) {
  const confirmed = window.confirm(
    `Delete draft ${receipt.goodsReceiptNumber}? This action cannot be undone.`,
  )
  if (!confirmed) return

  busyReceiptId.value = receipt.id
  operationError.value = ''
  try {
    await goodsReceiptService.delete(receipt.id)
    showSuccess(`${receipt.goodsReceiptNumber} was deleted.`)
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to delete the goods receipt.')
  } finally {
    busyReceiptId.value = null
  }
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-MY', { dateStyle: 'medium' }).format(
    new Date(`${value}T00:00:00`),
  )
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('en-MY', { maximumFractionDigits: 3 }).format(value)
}
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Warehouse operations</p>
        <h1>Goods Receiving</h1>
        <p class="page-description">
          Record supplier deliveries against issued purchase orders and post accepted quantities.
        </p>
      </div>
      <button
        v-if="canManageGoodsReceipts"
        class="button button-primary"
        type="button"
        :disabled="loading || !eligiblePurchaseOrders.length"
        @click="openCreateForm"
      >
        <span aria-hidden="true">+</span>
        New goods receipt
      </button>
    </header>

    <div class="summary-grid" aria-label="Goods receipt summary">
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Drafts</span>
        <strong>{{ draftCount }}</strong>
        <span>Deliveries still being checked</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Posted</span>
        <strong>{{ postedCount }}</strong>
        <span>Accepted into receiving history</span>
      </article>
      <article class="summary-card">
        <span class="summary-label">Open purchase orders</span>
        <strong>{{ openOrderCount }}</strong>
        <span>Issued orders awaiting delivery</span>
      </article>
    </div>

    <AppToast :toast="toast" @dismiss="dismissToast" />

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">&times;</button>
    </div>

    <section class="data-panel" aria-labelledby="goods-receipt-list-title">
      <div class="panel-toolbar goods-receipt-toolbar">
        <div>
          <h2 id="goods-receipt-list-title">Goods receipt register</h2>
          <p>{{ visibleReceipts.length }} records shown</p>
        </div>

        <div class="toolbar-actions goods-receipt-filters">
          <label class="search-control">
            <span class="sr-only">Search goods receipts</span>
            <span aria-hidden="true">&#8981;</span>
            <input
              v-model="search"
              type="search"
              placeholder="Search receipt, PO, supplier, or delivery note"
            />
          </label>
          <label class="relationship-filter-control">
            <span class="sr-only">Filter by status</span>
            <select v-model="selectedStatus" aria-label="Filter by status">
              <option v-for="status in statusFilters" :key="status.value" :value="status.value">
                {{ status.label }}
              </option>
            </select>
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading goods receipts</strong>
        <p>Retrieving receipts and open purchase orders.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Goods receipts could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadData">Try again</button>
      </div>

      <div v-else-if="!visibleReceipts.length" class="panel-state">
        <div class="empty-icon" aria-hidden="true">GR</div>
        <strong>{{
          goodsReceipts.length ? 'No matching receipts' : 'No goods receipts yet'
        }}</strong>
        <p v-if="!canManageGoodsReceipts">No goods receipts are available for review.</p>
        <p v-else-if="!eligiblePurchaseOrders.length">
          Issue a purchase order, or post its existing draft receipt, before recording a delivery.
        </p>
        <p v-else>Create a draft receipt when a supplier delivery arrives.</p>
        <button
          v-if="canManageGoodsReceipts && eligiblePurchaseOrders.length"
          class="button button-primary"
          type="button"
          @click="openCreateForm"
        >
          Create goods receipt
        </button>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Goods receipt</th>
              <th>Purchase order</th>
              <th>Supplier</th>
              <th>Delivery note</th>
              <th>Received date</th>
              <th>Items</th>
              <th>Status</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="receipt in visibleReceipts" :key="receipt.id">
              <td>
                <div class="goods-receipt-identity">
                  <span class="code-avatar">GR</span>
                  <span>
                    <strong>{{ receipt.goodsReceiptNumber }}</strong>
                    <small>{{ receipt.createdByName }}</small>
                  </span>
                </div>
              </td>
              <td>{{ receipt.purchaseOrderNumber }}</td>
              <td>
                <span class="table-primary">{{ receipt.supplierName }}</span>
                <small class="table-secondary">{{ receipt.supplierCode }}</small>
              </td>
              <td>{{ receipt.supplierDeliveryNoteNumber || 'Not provided' }}</td>
              <td>{{ formatDate(receipt.receivedDate) }}</td>
              <td>
                <strong>{{ receipt.items.length }}</strong>
                <small class="table-secondary">
                  {{
                    formatQuantity(
                      receipt.items.reduce((sum, item) => sum + item.quantityReceived, 0),
                    )
                  }}
                  received
                </small>
              </td>
              <td>
                <span
                  class="goods-receipt-status"
                  :class="`status-${receipt.status.toLowerCase()}`"
                >
                  {{ receipt.status }}
                </span>
              </td>
              <td>
                <div class="row-actions goods-receipt-row-actions">
                  <button class="text-button" type="button" @click="detailsReceipt = receipt">
                    View
                  </button>
                  <button
                    v-if="canManageGoodsReceipts && receipt.status === 'Draft'"
                    class="text-button"
                    type="button"
                    :disabled="busyReceiptId === receipt.id"
                    @click="openEditForm(receipt)"
                  >
                    Edit
                  </button>
                  <button
                    v-if="canManageGoodsReceipts && receipt.status === 'Draft'"
                    class="text-button text-button-positive"
                    type="button"
                    :disabled="busyReceiptId === receipt.id"
                    @click="postReceipt(receipt)"
                  >
                    Post
                  </button>
                  <button
                    v-if="canManageGoodsReceipts && receipt.status === 'Draft'"
                    class="text-button text-button-danger"
                    type="button"
                    :disabled="busyReceiptId === receipt.id"
                    @click="deleteReceipt(receipt)"
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

    <GoodsReceiptForm
      v-if="canManageGoodsReceipts && formOpen"
      :goods-receipt="editingReceipt"
      :purchase-orders="formPurchaseOrders"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveReceipt"
    />

    <GoodsReceiptDetails
      v-if="detailsReceipt"
      :goods-receipt="detailsReceipt"
      @close="detailsReceipt = null"
    />
  </section>
</template>
