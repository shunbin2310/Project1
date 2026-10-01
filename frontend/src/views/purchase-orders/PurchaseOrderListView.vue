<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import PurchaseOrderCancelDialog from '@/components/purchase-orders/PurchaseOrderCancelDialog.vue'
import PurchaseOrderDetails from '@/components/purchase-orders/PurchaseOrderDetails.vue'
import PurchaseOrderForm from '@/components/purchase-orders/PurchaseOrderForm.vue'
import WorkflowActionDialog from '@/components/purchase-requests/WorkflowActionDialog.vue'
import AppToast from '@/components/ui/AppToast.vue'
import { useToast } from '@/composables/useToast'
import { purchaseOrderService } from '@/services/purchaseOrderService'
import { quotationService } from '@/services/quotationService'
import { useAuthStore } from '@/stores/auth'
import { applicationRoles } from '@/types/auth'
import type {
  PurchaseOrder,
  PurchaseOrderFormValues,
  PurchaseOrderStatus,
} from '@/types/purchaseOrder'
import type { WorkflowActorIdentity, WorkflowAvailableAction } from '@/types/purchaseRequest'
import type { Quotation } from '@/types/quotation'
import { isWorkflowActionAuthorized } from '@/utils/workflowAuthorization'

const statusFilters: { value: '' | PurchaseOrderStatus; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Draft', label: 'Draft' },
  { value: 'PendingApproval', label: 'Pending approval' },
  { value: 'Approved', label: 'Approved' },
  { value: 'Issued', label: 'Issued' },
  { value: 'PartiallyReceived', label: 'Partially received' },
  { value: 'Received', label: 'Received' },
  { value: 'Cancelled', label: 'Cancelled' },
]

const authStore = useAuthStore()
const canManagePurchaseOrders = computed(
  () =>
    authStore.roles.includes(applicationRoles.admin) ||
    authStore.roles.includes(applicationRoles.procurementOfficer),
)
const actor = computed<WorkflowActorIdentity>(() => ({
  id: authStore.user?.id ?? 0,
  name: authStore.user?.fullName ?? 'Current user',
  roles: authStore.roles,
}))
const purchaseOrders = ref<PurchaseOrder[]>([])
const selectedQuotations = ref<Quotation[]>([])
const loading = ref(true)
const saving = ref(false)
const busyPurchaseOrderId = ref<number | null>(null)
const cancelling = ref(false)
const search = ref('')
const selectedStatus = ref<'' | PurchaseOrderStatus>('')
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const cancelError = ref('')
const formOpen = ref(false)
const editingPurchaseOrder = ref<PurchaseOrder | null>(null)
const detailsPurchaseOrder = ref<PurchaseOrder | null>(null)
const cancellingPurchaseOrder = ref<PurchaseOrder | null>(null)
const actionPurchaseOrder = ref<PurchaseOrder | null>(null)
const selectedAction = ref<WorkflowAvailableAction | null>(null)
const actioning = ref(false)
const actionError = ref('')
const { toast, showSuccess, dismissToast } = useToast()

const draftCount = computed(
  () => purchaseOrders.value.filter((order) => order.status === 'Draft').length,
)
const openDeliveryCount = computed(
  () =>
    purchaseOrders.value.filter(
      (order) => order.status === 'Issued' || order.status === 'PartiallyReceived',
    ).length,
)
const pendingApprovalCount = computed(
  () => purchaseOrders.value.filter((order) => order.status === 'PendingApproval').length,
)
const approvedCount = computed(
  () => purchaseOrders.value.filter((order) => order.status === 'Approved').length,
)
const usedQuotationIds = computed(
  () => new Set(purchaseOrders.value.map((order) => order.quotationId)),
)
const eligibleQuotations = computed(() => {
  const today = todayValue()
  return selectedQuotations.value.filter(
    (quotation) =>
      !usedQuotationIds.value.has(quotation.id) &&
      (!quotation.validUntil || quotation.validUntil >= today),
  )
})
const formQuotations = computed(() => {
  if (!editingPurchaseOrder.value) return eligibleQuotations.value

  const source = selectedQuotations.value.find(
    (quotation) => quotation.id === editingPurchaseOrder.value?.quotationId,
  )
  return source ? [source] : []
})
const visiblePurchaseOrders = computed(() => {
  const term = search.value.trim().toLowerCase()

  return purchaseOrders.value.filter((order) => {
    if (selectedStatus.value && order.status !== selectedStatus.value) return false
    if (!term) return true

    return [
      order.purchaseOrderNumber,
      order.purchaseRequestNumber,
      order.quotationNumber,
      order.supplierCode,
      order.supplierName,
      order.supplierQuotationReference ?? '',
    ].some((value) => value.toLowerCase().includes(term))
  })
})

function canSubmitPurchaseOrder(order: PurchaseOrder) {
  if (!canManagePurchaseOrders.value || order.status !== 'Draft') return false
  if (authStore.roles.includes(applicationRoles.admin)) return true

  const submitAction = order.workflow?.availableActions.find((action) => action.code === 'SUBMIT')
  return submitAction
    ? isWorkflowActionAuthorized(submitAction, actor.value)
    : order.createdByUserId === actor.value.id
}

onMounted(loadData)

async function loadData() {
  loading.value = true
  loadError.value = ''

  try {
    const [orderRecords, quotationRecords] = await Promise.all([
      purchaseOrderService.getAll(),
      canManagePurchaseOrders.value
        ? quotationService.getAll({ status: 'Selected' })
        : Promise.resolve([]),
    ])
    purchaseOrders.value = orderRecords
    selectedQuotations.value = quotationRecords
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load purchase orders.')
  } finally {
    loading.value = false
  }
}

function openCreateForm() {
  editingPurchaseOrder.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(order: PurchaseOrder) {
  editingPurchaseOrder.value = order
  detailsPurchaseOrder.value = null
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingPurchaseOrder.value = null
  formError.value = ''
}

async function savePurchaseOrder(values: PurchaseOrderFormValues, submitAfterSave: boolean) {
  saving.value = true
  formError.value = ''
  const wasEditing = editingPurchaseOrder.value !== null

  try {
    let saved: PurchaseOrder
    if (editingPurchaseOrder.value) {
      saved = await purchaseOrderService.update(editingPurchaseOrder.value.id, {
        orderDate: values.orderDate,
        expectedDeliveryDate: values.expectedDeliveryDate,
        deliveryAddress: values.deliveryAddress,
        notes: values.notes,
      })
    } else {
      saved = await purchaseOrderService.create(values)
    }

    if (submitAfterSave) {
      saved = await purchaseOrderService.executeAction(saved.id, 'SUBMIT', { comment: null })
    }

    formOpen.value = false
    editingPurchaseOrder.value = null
    showSuccess(
      submitAfterSave
        ? `${saved.purchaseOrderNumber} was submitted for approval.`
        : wasEditing
          ? `${saved.purchaseOrderNumber} draft was updated.`
          : `${saved.purchaseOrderNumber} was created as a draft.`,
    )
    await loadData()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the purchase order.')
  } finally {
    saving.value = false
  }
}

async function submitPurchaseOrder(order: PurchaseOrder) {
  busyPurchaseOrderId.value = order.id
  operationError.value = ''
  try {
    await purchaseOrderService.executeAction(order.id, 'SUBMIT', { comment: null })
    showSuccess(`${order.purchaseOrderNumber} was submitted for approval.`)
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to submit the purchase order.')
  } finally {
    busyPurchaseOrderId.value = null
  }
}

function openWorkflowAction(order: PurchaseOrder, action: WorkflowAvailableAction) {
  actionPurchaseOrder.value = order
  selectedAction.value = action
  actionError.value = ''
}

function closeWorkflowAction() {
  if (actioning.value) return
  selectedAction.value = null
  actionPurchaseOrder.value = null
  actionError.value = ''
}

async function executeWorkflowAction(comment: string | null) {
  if (!actionPurchaseOrder.value || !selectedAction.value) return
  const order = actionPurchaseOrder.value
  const action = selectedAction.value

  actioning.value = true
  actionError.value = ''
  try {
    await purchaseOrderService.executeAction(order.id, action.code, { comment })
    selectedAction.value = null
    actionPurchaseOrder.value = null
    detailsPurchaseOrder.value = null
    showSuccess(`${order.purchaseOrderNumber}: ${action.name} completed.`)
    await loadData()
  } catch (error) {
    actionError.value = getErrorMessage(error, 'Unable to execute the workflow action.')
  } finally {
    actioning.value = false
  }
}

async function issuePurchaseOrder(order: PurchaseOrder) {
  const confirmed = window.confirm(
    `Issue ${order.purchaseOrderNumber} to ${order.supplierName}? The order can no longer be edited or deleted.`,
  )
  if (!confirmed) return

  busyPurchaseOrderId.value = order.id
  operationError.value = ''
  try {
    await purchaseOrderService.issue(order.id)
    showSuccess(`${order.purchaseOrderNumber} was issued and its supplier email was queued.`)
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to issue the purchase order.')
  } finally {
    busyPurchaseOrderId.value = null
  }
}

function openCancelDialog(order: PurchaseOrder) {
  cancelError.value = ''
  cancellingPurchaseOrder.value = order
}

function closeCancelDialog() {
  if (cancelling.value) return
  cancellingPurchaseOrder.value = null
  cancelError.value = ''
}

async function cancelPurchaseOrder(reason: string) {
  const order = cancellingPurchaseOrder.value
  if (!order) return

  cancelling.value = true
  cancelError.value = ''
  try {
    await purchaseOrderService.cancel(order.id, reason)
    cancellingPurchaseOrder.value = null
    showSuccess(`${order.purchaseOrderNumber} was cancelled.`)
    await loadData()
  } catch (error) {
    cancelError.value = getErrorMessage(error, 'Unable to cancel the purchase order.')
  } finally {
    cancelling.value = false
  }
}

async function deletePurchaseOrder(order: PurchaseOrder) {
  const confirmed = window.confirm(
    `Delete draft ${order.purchaseOrderNumber}? This action cannot be undone.`,
  )
  if (!confirmed) return

  busyPurchaseOrderId.value = order.id
  operationError.value = ''
  try {
    await purchaseOrderService.delete(order.id)
    showSuccess(`${order.purchaseOrderNumber} was deleted.`)
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to delete the purchase order.')
  } finally {
    busyPurchaseOrderId.value = null
  }
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback
}

function todayValue() {
  const today = new Date()
  const offset = today.getTimezoneOffset() * 60_000
  return new Date(today.getTime() - offset).toISOString().slice(0, 10)
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat('en-MY', { style: 'currency', currency: 'MYR' }).format(value)
}

function formatDate(value: string | null) {
  if (!value) return 'Not set'
  return new Intl.DateTimeFormat('en-MY', { dateStyle: 'medium' }).format(
    new Date(`${value}T00:00:00`),
  )
}

function statusLabel(status: PurchaseOrderStatus) {
  if (status === 'PartiallyReceived') return 'Partially received'
  if (status === 'PendingApproval') return 'Pending approval'
  return status
}
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Purchasing execution</p>
        <h1>Purchase Orders</h1>
        <p class="page-description">
          Convert selected supplier quotations into controlled orders and track their issue status.
        </p>
      </div>
      <button
        v-if="canManagePurchaseOrders"
        class="button button-primary"
        type="button"
        :disabled="loading || !eligibleQuotations.length"
        @click="openCreateForm"
      >
        <span aria-hidden="true">+</span>
        New purchase order
      </button>
    </header>

    <div class="summary-grid" aria-label="Purchase order summary">
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Drafts</span>
        <strong>{{ draftCount }}</strong>
        <span>Orders still being prepared</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Pending approval</span>
        <strong>{{ pendingApprovalCount }}</strong>
        <span>Waiting for Purchase Order Approver</span>
      </article>
      <article class="summary-card">
        <span class="summary-label">Ready / open delivery</span>
        <strong>{{ approvedCount + openDeliveryCount }}</strong>
        <span>Approved, issued, or partially received</span>
      </article>
    </div>

    <AppToast :toast="toast" @dismiss="dismissToast" />

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">&times;</button>
    </div>

    <section class="data-panel" aria-labelledby="purchase-order-list-title">
      <div class="panel-toolbar purchase-order-toolbar">
        <div>
          <h2 id="purchase-order-list-title">Purchase order register</h2>
          <p>{{ visiblePurchaseOrders.length }} records shown</p>
        </div>

        <div class="toolbar-actions purchase-order-filters">
          <label class="search-control">
            <span class="sr-only">Search purchase orders</span>
            <span aria-hidden="true">⌕</span>
            <input
              v-model="search"
              type="search"
              placeholder="Search order, request, or supplier"
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
        <strong>Loading purchase orders</strong>
        <p>Retrieving orders and selected supplier quotations.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Purchase orders could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadData">Try again</button>
      </div>

      <div v-else-if="!visiblePurchaseOrders.length" class="panel-state">
        <div class="empty-icon" aria-hidden="true">PO</div>
        <strong>{{
          purchaseOrders.length ? 'No matching purchase orders' : 'No purchase orders yet'
        }}</strong>
        <p v-if="!canManagePurchaseOrders">No purchase orders are available for review.</p>
        <p v-else-if="!eligibleQuotations.length">
          Select a winning supplier quotation before creating a purchase order.
        </p>
        <p v-else>Create a draft order from a selected supplier quotation.</p>
        <button
          v-if="canManagePurchaseOrders && eligibleQuotations.length"
          class="button button-primary"
          type="button"
          @click="openCreateForm"
        >
          Create purchase order
        </button>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Purchase order</th>
              <th>Supplier</th>
              <th>Purchase request</th>
              <th>Order date</th>
              <th>Expected delivery</th>
              <th>Total</th>
              <th>Status</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="order in visiblePurchaseOrders" :key="order.id">
              <td>
                <div class="purchase-order-identity">
                  <span class="code-avatar">PO</span>
                  <span>
                    <strong>{{ order.purchaseOrderNumber }}</strong>
                    <small>{{ order.quotationNumber }}</small>
                  </span>
                </div>
              </td>
              <td>
                <span class="table-primary">{{ order.supplierName }}</span>
                <small class="table-secondary">{{ order.supplierCode }}</small>
              </td>
              <td>{{ order.purchaseRequestNumber }}</td>
              <td>{{ formatDate(order.orderDate) }}</td>
              <td>{{ formatDate(order.expectedDeliveryDate) }}</td>
              <td>
                <strong>{{ formatCurrency(order.totalAmount) }}</strong>
              </td>
              <td>
                <span class="purchase-order-status" :class="`status-${order.status.toLowerCase()}`">
                  {{ statusLabel(order.status) }}
                </span>
              </td>
              <td>
                <div class="row-actions purchase-order-row-actions">
                  <button class="text-button" type="button" @click="detailsPurchaseOrder = order">
                    View
                  </button>
                  <button
                    v-if="canManagePurchaseOrders && order.status === 'Draft'"
                    class="text-button"
                    type="button"
                    :disabled="busyPurchaseOrderId === order.id"
                    @click="openEditForm(order)"
                  >
                    Edit
                  </button>
                  <button
                    v-if="canSubmitPurchaseOrder(order)"
                    class="text-button text-button-positive"
                    type="button"
                    :disabled="busyPurchaseOrderId === order.id"
                    @click="submitPurchaseOrder(order)"
                  >
                    Submit
                  </button>
                  <button
                    v-if="canManagePurchaseOrders && order.status === 'Approved'"
                    class="text-button text-button-positive"
                    type="button"
                    :disabled="busyPurchaseOrderId === order.id"
                    @click="issuePurchaseOrder(order)"
                  >
                    Issue
                  </button>
                  <button
                    v-if="
                      canManagePurchaseOrders &&
                      (order.status === 'Approved' || order.status === 'Issued')
                    "
                    class="text-button text-button-danger"
                    type="button"
                    @click="openCancelDialog(order)"
                  >
                    Cancel
                  </button>
                  <button
                    v-if="canManagePurchaseOrders && order.status === 'Draft'"
                    class="text-button text-button-danger"
                    type="button"
                    :disabled="busyPurchaseOrderId === order.id"
                    @click="deletePurchaseOrder(order)"
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

    <PurchaseOrderForm
      v-if="canManagePurchaseOrders && formOpen"
      :purchase-order="editingPurchaseOrder"
      :quotations="formQuotations"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="savePurchaseOrder"
    />

    <PurchaseOrderDetails
      v-if="detailsPurchaseOrder"
      :purchase-order="detailsPurchaseOrder"
      :actor="actor"
      @close="detailsPurchaseOrder = null"
      @action="(action) => openWorkflowAction(detailsPurchaseOrder!, action)"
    />

    <WorkflowActionDialog
      v-if="selectedAction"
      :action="selectedAction"
      :actor="actor"
      :saving="actioning"
      :error-message="actionError"
      @cancel="closeWorkflowAction"
      @execute="executeWorkflowAction"
    />

    <PurchaseOrderCancelDialog
      v-if="canManagePurchaseOrders && cancellingPurchaseOrder"
      :purchase-order="cancellingPurchaseOrder"
      :cancelling="cancelling"
      :error-message="cancelError"
      @close="closeCancelDialog"
      @confirm="cancelPurchaseOrder"
    />
  </section>
</template>
