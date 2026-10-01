<script setup lang="ts">
import type {
  WorkflowActorIdentity,
  WorkflowAvailableAction,
} from '@/types/purchaseRequest'
import type { PurchaseOrder, PurchaseOrderStatus } from '@/types/purchaseOrder'
import { isWorkflowActionAuthorized } from '@/utils/workflowAuthorization'

const props = defineProps<{
  purchaseOrder: PurchaseOrder
  actor: WorkflowActorIdentity
}>()

const emit = defineEmits<{
  close: []
  action: [action: WorkflowAvailableAction]
}>()

function isAuthorized(action: WorkflowAvailableAction) {
  return isWorkflowActionAuthorized(action, props.actor)
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

function formatDateTime(value: string | null) {
  if (!value) return 'Not recorded'
  return new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('en-MY', { maximumFractionDigits: 3 }).format(value)
}

function statusLabel(status: PurchaseOrderStatus) {
  if (status === 'PartiallyReceived') return 'Partially received'
  if (status === 'PendingApproval') return 'Pending approval'
  return status
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('close')">
    <section
      class="modal-card purchase-order-details-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="purchase-order-details-title"
    >
      <header class="modal-header">
        <div class="purchase-order-details-title">
          <p class="eyebrow">Purchase order</p>
          <div>
            <h2 id="purchase-order-details-title">{{ purchaseOrder.purchaseOrderNumber }}</h2>
            <span
              class="purchase-order-status"
              :class="`status-${purchaseOrder.status.toLowerCase()}`"
            >
              {{ statusLabel(purchaseOrder.status) }}
            </span>
          </div>
        </div>
        <button class="icon-button" type="button" aria-label="Close details" @click="emit('close')">
          &times;
        </button>
      </header>

      <div class="purchase-order-details">
        <section class="purchase-order-summary">
          <div class="purchase-order-summary-primary">
            <div>
              <span>Supplier</span>
              <strong>{{ purchaseOrder.supplierName }}</strong>
              <small>{{ purchaseOrder.supplierCode }}</small>
            </div>
            <div>
              <span>Purchase request</span>
              <strong>{{ purchaseOrder.purchaseRequestNumber }}</strong>
              <small>Source request</small>
            </div>
            <div>
              <span>Selected quotation</span>
              <strong>{{ purchaseOrder.quotationNumber }}</strong>
              <small>{{
                purchaseOrder.supplierQuotationReference || 'No supplier quotation reference'
              }}</small>
            </div>
          </div>
          <div class="purchase-order-summary-meta">
            <div>
              <span>Order date</span>
              <strong>{{ formatDate(purchaseOrder.orderDate) }}</strong>
            </div>
            <div>
              <span>Expected delivery</span>
              <strong>{{ formatDate(purchaseOrder.expectedDeliveryDate) }}</strong>
            </div>
            <div>
              <span>Total amount</span>
              <strong>{{ formatCurrency(purchaseOrder.totalAmount) }}</strong>
            </div>
          </div>
        </section>

        <section v-if="purchaseOrder.workflow" class="workflow-current-card">
          <div>
            <span class="summary-label">Current workflow step</span>
            <strong>{{ purchaseOrder.workflow.currentStepName }}</strong>
            <small>
              {{ purchaseOrder.workflow.templateName }} &middot; Version
              {{ purchaseOrder.workflow.templateVersion }}
            </small>
          </div>
          <span
            class="workflow-step-badge"
            :class="`step-${purchaseOrder.workflow.currentStepCode.toLowerCase()}`"
          >
            {{ purchaseOrder.workflow.currentStepCode.replace(/_/g, ' ') }}
          </span>
        </section>

        <section
          v-if="
            purchaseOrder.workflow &&
            purchaseOrder.workflow.currentStepCode !== 'DRAFT' &&
            purchaseOrder.workflow.availableActions.length
          "
          class="workflow-actions-panel"
        >
          <div class="details-section-heading">
            <div>
              <h3>Available actions</h3>
              <p>Current identity: {{ actor.name }}</p>
            </div>
          </div>
          <div class="workflow-action-buttons">
            <button
              v-for="action in purchaseOrder.workflow.availableActions"
              :key="`${action.code}-${action.toStepCode}`"
              class="button"
              :class="action.code === 'REJECT' ? 'button-danger' : 'button-primary'"
              type="button"
              :disabled="!isAuthorized(action)"
              @click="emit('action', action)"
            >
              {{ action.name }}
            </button>
          </div>
        </section>

        <section class="details-section">
          <div class="details-section-heading">
            <div>
              <h3>Ordered items</h3>
              <p>Immutable product and price snapshot from the selected quotation</p>
            </div>
          </div>
          <div class="details-table table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Product</th>
                  <th>Quantity</th>
                  <th>Unit price</th>
                  <th>Line total</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in purchaseOrder.items" :key="item.id">
                  <td>
                    <strong>{{ item.productName }}</strong>
                    <small>{{ item.productCode }}</small>
                  </td>
                  <td>{{ formatQuantity(item.quantity) }} {{ item.unitOfMeasureCode }}</td>
                  <td>{{ formatCurrency(item.unitPrice) }}</td>
                  <td>
                    <strong>{{ formatCurrency(item.lineTotal) }}</strong>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <div class="purchase-order-copy-grid">
          <section class="details-section">
            <div class="details-section-heading"><h3>Delivery address</h3></div>
            <p class="justification-copy">
              {{ purchaseOrder.deliveryAddress || 'No delivery address provided.' }}
            </p>
          </section>
          <section class="details-section">
            <div class="details-section-heading"><h3>Order notes</h3></div>
            <p class="justification-copy">{{ purchaseOrder.notes || 'No notes provided.' }}</p>
          </section>
        </div>

        <section v-if="purchaseOrder.status === 'Cancelled'" class="purchase-order-cancellation">
          <strong>Cancellation reason</strong>
          <p>{{ purchaseOrder.cancellationReason }}</p>
        </section>

        <section v-if="purchaseOrder.workflow" class="details-section">
          <div class="details-section-heading">
            <div>
              <h3>Workflow history</h3>
              <p>Immutable approval audit trail</p>
            </div>
          </div>
          <ol class="workflow-timeline">
            <li v-for="entry in purchaseOrder.workflow.history" :key="entry.id">
              <span class="timeline-marker" aria-hidden="true"></span>
              <div class="timeline-content">
                <div>
                  <strong>{{ entry.actionCode }}</strong>
                  <span>{{ entry.toStepCode.replace(/_/g, ' ') }}</span>
                </div>
                <p v-if="entry.comment">{{ entry.comment }}</p>
                <small>{{ entry.actionBy }} &middot; {{ formatDateTime(entry.actionAtUtc) }}</small>
              </div>
            </li>
          </ol>
        </section>

        <section class="purchase-order-audit" aria-label="Purchase order audit information">
          <div>
            <span>Created</span>
            <strong>{{ purchaseOrder.createdByName }}</strong>
            <small>{{ formatDateTime(purchaseOrder.createdAtUtc) }}</small>
          </div>
          <div v-if="purchaseOrder.issuedAtUtc">
            <span>Issued</span>
            <strong>{{ purchaseOrder.issuedByName }}</strong>
            <small>{{ formatDateTime(purchaseOrder.issuedAtUtc) }}</small>
          </div>
          <div v-if="purchaseOrder.cancelledAtUtc">
            <span>Cancelled</span>
            <strong>{{ purchaseOrder.cancelledByName }}</strong>
            <small>{{ formatDateTime(purchaseOrder.cancelledAtUtc) }}</small>
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
