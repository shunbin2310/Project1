<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

import PurchaseRequestDetails from '@/components/purchase-requests/PurchaseRequestDetails.vue'
import PurchaseRequestForm from '@/components/purchase-requests/PurchaseRequestForm.vue'
import WorkflowActionDialog from '@/components/purchase-requests/WorkflowActionDialog.vue'
import { productService } from '@/services/productService'
import { purchaseRequestService } from '@/services/purchaseRequestService'
import { useAuthStore } from '@/stores/auth'
import type { Product } from '@/types/product'
import type {
  PurchaseRequest,
  PurchaseRequestFormValues,
  WorkflowActorIdentity,
  WorkflowAvailableAction,
} from '@/types/purchaseRequest'
import {
  getAuthorizedWorkflowActions,
  isWorkflowActionDirectlyAssignedToActor,
} from '@/utils/workflowAuthorization'

const requests = ref<PurchaseRequest[]>([])
const products = ref<Product[]>([])
const loading = ref(true)
const saving = ref(false)
const actioning = ref(false)
const loadError = ref('')
const formError = ref('')
const actionError = ref('')
const successMessage = ref('')
const selectedRequest = ref<PurchaseRequest | null>(null)
const editingRequest = ref<PurchaseRequest | null>(null)
const selectedAction = ref<WorkflowAvailableAction | null>(null)
const authStore = useAuthStore()
let successTimer: number | undefined

const actor = computed<WorkflowActorIdentity>(() => ({
  id: authStore.user?.id ?? 0,
  name: authStore.user?.fullName ?? 'Current user',
  roles: authStore.roles,
}))
const currentTasks = computed(() =>
  requests.value.filter((request) => authorizedActions(request).length > 0),
)

onMounted(loadTasks)
onUnmounted(() => window.clearTimeout(successTimer))

function authorizedActions(request: PurchaseRequest) {
  return getAuthorizedWorkflowActions(request.workflow.availableActions, actor.value)
}

function canEditTask(request: PurchaseRequest) {
  return (
    request.workflow.currentStepCode === 'DRAFT' &&
    request.workflow.availableActions.some(
      (action) =>
        action.code === 'SUBMIT' && isWorkflowActionDirectlyAssignedToActor(action, actor.value),
    )
  )
}

async function loadTasks() {
  loading.value = true
  loadError.value = ''

  try {
    const [requestRecords, productRecords] = await Promise.all([
      purchaseRequestService.getAll(),
      productService.getAll(true),
    ])
    requests.value = requestRecords
    products.value = productRecords
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load your workflow tasks.')
  } finally {
    loading.value = false
  }
}

function openDetails(request: PurchaseRequest) {
  selectedRequest.value = request
}

function openEditForm(request: PurchaseRequest) {
  if (!canEditTask(request)) return
  selectedRequest.value = null
  editingRequest.value = request
  formError.value = ''
}

function closeEditForm() {
  if (saving.value) return
  editingRequest.value = null
  formError.value = ''
}

async function saveRequest(values: PurchaseRequestFormValues, submitAfterSave: boolean) {
  if (!editingRequest.value) return

  saving.value = true
  formError.value = ''
  const requestNumber = editingRequest.value.requestNumber

  try {
    let updated = await purchaseRequestService.update(editingRequest.value.id, values)

    if (submitAfterSave) {
      updated = await purchaseRequestService.executeAction(updated.id, 'SUBMIT', {
        comment: 'Updated and submitted from My Tasks.',
      })
    }

    editingRequest.value = null
    showSuccess(
      submitAfterSave
        ? `${updated.requestNumber} was submitted for department review.`
        : `${requestNumber} draft was updated.`,
    )
    await loadTasks()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to update the purchase request.')
  } finally {
    saving.value = false
  }
}

function closeDetails() {
  if (actioning.value) return
  selectedRequest.value = null
  selectedAction.value = null
}

function openAction(action: WorkflowAvailableAction) {
  selectedAction.value = action
  actionError.value = ''
}

function closeAction() {
  if (actioning.value) return
  selectedAction.value = null
  actionError.value = ''
}

async function executeAction(comment: string | null) {
  if (!selectedRequest.value || !selectedAction.value) return
  const action = selectedAction.value

  actioning.value = true
  actionError.value = ''

  try {
    const updated = await purchaseRequestService.executeAction(
      selectedRequest.value.id,
      action.code,
      { comment },
    )
    selectedAction.value = null
    selectedRequest.value = null
    showSuccess(`${updated.requestNumber}: ${action.name} completed.`)
    await loadTasks()
  } catch (error) {
    actionError.value = getErrorMessage(error, 'Unable to execute the workflow action.')
  } finally {
    actioning.value = false
  }
}

function showSuccess(message: string) {
  window.clearTimeout(successTimer)
  successMessage.value = message
  successTimer = window.setTimeout(() => {
    if (successMessage.value === message) successMessage.value = ''
  }, 3500)
}

function dismissSuccess() {
  window.clearTimeout(successTimer)
  successMessage.value = ''
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback
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

function stepClass(stepCode: string) {
  return `step-${stepCode.toLowerCase()}`
}
</script>

<template>
  <section class="page-section my-task-page">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Workflow inbox</p>
        <h1>My Tasks</h1>
        <p class="page-description">
          Review workflow items that are currently waiting for an action from your account.
        </p>
      </div>
    </header>

    <section class="data-panel" aria-labelledby="current-tasks-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="current-tasks-title">Pending workflow tasks</h2>
          <p v-if="loading">Checking workflow assignments</p>
          <p v-else>
            {{ currentTasks.length }} {{ currentTasks.length === 1 ? 'task' : 'tasks' }} assigned to
            your account
          </p>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading your tasks</strong>
        <p>Checking workflow assignments.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Tasks could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadTasks">Try again</button>
      </div>

      <div v-else-if="currentTasks.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">OK</div>
        <strong>You're all caught up</strong>
        <p>No workflow item currently needs an action from you.</p>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Request</th>
              <th>Requester</th>
              <th>Department</th>
              <th>Required date</th>
              <th>Estimated total</th>
              <th>Current step</th>
              <th><span class="sr-only">Details</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="task in currentTasks" :key="task.id">
              <td>
                <div class="purchase-request-identity">
                  <span class="code-avatar">RQ</span>
                  <span>
                    <strong>{{ task.requestNumber }}</strong>
                    <small>{{ task.items.length }} items</small>
                  </span>
                </div>
              </td>
              <td>{{ task.requesterName || 'Not set' }}</td>
              <td>
                <div class="contact-stack">
                  <span>{{ task.departmentName || 'Not set' }}</span>
                  <small v-if="task.departmentCode">{{ task.departmentCode }}</small>
                </div>
              </td>
              <td>{{ formatDate(task.requiredDate) }}</td>
              <td>{{ formatCurrency(task.estimatedTotal) }}</td>
              <td>
                <span class="workflow-step-badge" :class="stepClass(task.workflow.currentStepCode)">
                  {{ task.workflow.currentStepCode.replace(/_/g, ' ') }}
                </span>
              </td>
              <td>
                <div class="row-actions">
                  <button
                    v-if="!canEditTask(task)"
                    class="text-button"
                    type="button"
                    @click="openDetails(task)"
                  >
                    Details
                  </button>
                  <button
                    v-if="canEditTask(task)"
                    class="text-button"
                    type="button"
                    @click="openEditForm(task)"
                  >
                    Edit
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <Transition name="toast">
      <div v-if="successMessage" class="success-toast" role="status" aria-live="polite">
        <span class="success-toast-icon" aria-hidden="true">OK</span>
        <div>
          <strong>Action completed</strong>
          <p>{{ successMessage }}</p>
        </div>
        <button type="button" aria-label="Dismiss success message" @click="dismissSuccess">
          &times;
        </button>
      </div>
    </Transition>

    <PurchaseRequestForm
      v-if="editingRequest"
      :purchase-request="editingRequest"
      :products="products"
      :saving="saving"
      :error-message="formError"
      @cancel="closeEditForm"
      @save="saveRequest"
    />

    <PurchaseRequestDetails
      v-if="selectedRequest"
      :purchase-request="selectedRequest"
      :actor="actor"
      :can-manage-draft="false"
      :deleting="false"
      @close="closeDetails"
      @action="openAction"
    />

    <WorkflowActionDialog
      v-if="selectedAction"
      :action="selectedAction"
      :actor="actor"
      :saving="actioning"
      :error-message="actionError"
      @cancel="closeAction"
      @execute="executeAction"
    />
  </section>
</template>
