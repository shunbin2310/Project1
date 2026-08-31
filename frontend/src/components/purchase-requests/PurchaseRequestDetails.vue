<script setup lang="ts">
import { computed } from 'vue'

import type {
  PurchaseRequest,
  WorkflowActorIdentity,
  WorkflowAvailableAction,
} from '@/types/purchaseRequest'
import { isWorkflowActionAuthorized } from '@/utils/workflowAuthorization'

const props = defineProps<{
  purchaseRequest: PurchaseRequest
  actor: WorkflowActorIdentity
  deleting: boolean
  canManageDraft: boolean
}>()

const emit = defineEmits<{
  close: []
  edit: []
  delete: []
  action: [action: WorkflowAvailableAction]
}>()

const isDraft = computed(() => props.purchaseRequest.workflow.currentStepCode === 'DRAFT')
const canEditDraft = computed(() => isDraft.value && props.canManageDraft)

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

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('en-MY', { maximumFractionDigits: 3 }).format(value)
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('close')">
    <section
      class="modal-card purchase-request-details-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="purchase-request-details-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Purchase request</p>
          <h2 id="purchase-request-details-title">{{ purchaseRequest.requestNumber }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close details" @click="emit('close')">
          &times;
        </button>
      </header>

      <div class="purchase-request-details">
        <section class="request-overview">
          <div>
            <span>Requester</span>
            <strong>{{ purchaseRequest.requesterName || 'Not set' }}</strong>
          </div>
          <div>
            <span>Department</span>
            <strong>{{ purchaseRequest.departmentName || 'Not set' }}</strong>
            <small v-if="purchaseRequest.departmentCode">{{
              purchaseRequest.departmentCode
            }}</small>
          </div>
          <div>
            <span>Required date</span>
            <strong>{{ formatDate(purchaseRequest.requiredDate) }}</strong>
          </div>
          <div>
            <span>Estimated total</span>
            <strong>{{ formatCurrency(purchaseRequest.estimatedTotal) }}</strong>
          </div>
        </section>

        <section class="workflow-current-card">
          <div>
            <span class="summary-label">Current workflow step</span>
            <strong>{{ purchaseRequest.workflow.currentStepName }}</strong>
            <small>
              {{ purchaseRequest.workflow.templateName }} · Version
              {{ purchaseRequest.workflow.templateVersion }}
            </small>
          </div>
          <span
            class="workflow-step-badge"
            :class="`step-${purchaseRequest.workflow.currentStepCode.toLowerCase()}`"
          >
            {{ purchaseRequest.workflow.currentStepCode.replace(/_/g, ' ') }}
          </span>
        </section>

        <section
          v-if="!isDraft && purchaseRequest.workflow.availableActions.length"
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
              v-for="action in purchaseRequest.workflow.availableActions"
              :key="`${action.code}-${action.toStepCode}`"
              class="button"
              :class="action.code === 'REJECT' ? 'button-danger' : 'button-primary'"
              type="button"
              :disabled="!isAuthorized(action)"
              :title="isAuthorized(action) ? '' : `${actor.name} is not authorized for this action`"
              @click="emit('action', action)"
            >
              {{ action.name }}
            </button>
          </div>
          <p
            v-if="!purchaseRequest.workflow.availableActions.some(isAuthorized)"
            class="authorization-note"
          >
            Your current account is not authorized to perform an action at this step.
          </p>
        </section>

        <section class="details-section">
          <div class="details-section-heading">
            <div>
              <h3>Requested items</h3>
              <p>{{ purchaseRequest.items.length }} line items</p>
            </div>
          </div>
          <div v-if="purchaseRequest.items.length" class="table-scroll details-table">
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
                <tr v-for="item in purchaseRequest.items" :key="item.id">
                  <td>
                    <strong>{{ item.productName }}</strong>
                    <small>{{ item.productCode }}</small>
                  </td>
                  <td>{{ formatQuantity(item.quantity) }} {{ item.unitOfMeasureCode }}</td>
                  <td>{{ formatCurrency(item.estimatedUnitPrice) }}</td>
                  <td>{{ formatCurrency(item.lineTotal) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="details-empty">No items have been added to this draft.</p>
        </section>

        <section class="details-section">
          <div class="details-section-heading">
            <div>
              <h3>Business justification</h3>
            </div>
          </div>
          <p class="justification-copy">
            {{ purchaseRequest.justification || 'No justification provided.' }}
          </p>
        </section>

        <section class="details-section">
          <div class="details-section-heading">
            <div>
              <h3>Workflow history</h3>
              <p>Immutable audit trail for this request</p>
            </div>
          </div>
          <ol class="workflow-timeline">
            <li v-for="entry in purchaseRequest.workflow.history" :key="entry.id">
              <span class="timeline-marker" aria-hidden="true"></span>
              <div class="timeline-content">
                <div>
                  <strong>{{ entry.actionCode }}</strong>
                  <span>{{ entry.toStepCode.replace(/_/g, ' ') }}</span>
                </div>
                <p v-if="entry.comment">{{ entry.comment }}</p>
                <small>{{ entry.actionBy }} · {{ formatDateTime(entry.actionAtUtc) }}</small>
              </div>
            </li>
          </ol>
        </section>

        <footer class="details-footer">
          <div>
            <button
              v-if="canEditDraft"
              class="text-button text-button-danger"
              type="button"
              :disabled="deleting"
              @click="emit('delete')"
            >
              {{ deleting ? 'Deleting...' : 'Delete draft' }}
            </button>
          </div>
          <div class="modal-actions">
            <button class="button button-secondary" type="button" @click="emit('close')">
              Close
            </button>
            <button
              v-if="canEditDraft"
              class="button button-primary"
              type="button"
              @click="emit('edit')"
            >
              Edit draft
            </button>
          </div>
        </footer>
      </div>
    </section>
  </div>
</template>
