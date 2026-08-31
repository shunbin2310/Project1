<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import PurchaseRequestDetails from '@/components/purchase-requests/PurchaseRequestDetails.vue'
import PurchaseRequestForm from '@/components/purchase-requests/PurchaseRequestForm.vue'
import WorkflowActionDialog from '@/components/purchase-requests/WorkflowActionDialog.vue'
import { productService } from '@/services/productService'
import { purchaseRequestService } from '@/services/purchaseRequestService'
import { useAuthStore } from '@/stores/auth'
import { applicationRoles } from '@/types/auth'
import type { Product } from '@/types/product'
import type {
  PurchaseRequest,
  PurchaseRequestFormValues,
  WorkflowActorIdentity,
  WorkflowAvailableAction,
} from '@/types/purchaseRequest'
import { isWorkflowActionDirectlyAssignedToActor } from '@/utils/workflowAuthorization'

const stepFilters = [
  { value: '', label: 'All steps' },
  { value: 'DRAFT', label: 'Draft' },
  { value: 'DEPARTMENT_REVIEW', label: 'Department Review' },
  { value: 'FINANCE_REVIEW', label: 'Finance Review' },
  { value: 'APPROVED', label: 'Approved' },
  { value: 'REJECTED', label: 'Rejected' },
]

const requests = ref<PurchaseRequest[]>([])
const products = ref<Product[]>([])
const loading = ref(true)
const saving = ref(false)
const deleting = ref(false)
const actioning = ref(false)
const search = ref('')
const selectedStep = ref('')
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const actionError = ref('')
const successMessage = ref('')
const formOpen = ref(false)
const selectedRequest = ref<PurchaseRequest | null>(null)
const editingRequest = ref<PurchaseRequest | null>(null)
const selectedAction = ref<WorkflowAvailableAction | null>(null)
const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()
let successTimer: number | undefined

const draftCount = computed(
  () => requests.value.filter((request) => request.workflow.currentStepCode === 'DRAFT').length,
)
const reviewCount = computed(
  () =>
    requests.value.filter((request) =>
      ['DEPARTMENT_REVIEW', 'FINANCE_REVIEW'].includes(request.workflow.currentStepCode),
    ).length,
)
const completedCount = computed(
  () => requests.value.filter((request) => request.workflow.status === 'Completed').length,
)

const actor = computed<WorkflowActorIdentity>(() => ({
  id: authStore.user?.id ?? 0,
  name: authStore.user?.fullName ?? 'Current user',
  roles: authStore.roles,
}))
const canManageRequests = computed(() =>
  authStore.hasAnyRole([applicationRoles.requester, applicationRoles.admin]),
)

const visibleRequests = computed(() => {
  const term = search.value.trim().toLowerCase()

  return requests.value.filter((request) => {
    if (selectedStep.value && request.workflow.currentStepCode !== selectedStep.value) return false
    if (!term) return true

    return [
      request.requestNumber,
      request.requesterName ?? '',
      request.departmentCode ?? '',
      request.departmentName ?? '',
      request.workflow.currentStepName,
    ].some((value) => value.toLowerCase().includes(term))
  })
})

onMounted(async () => {
  await loadData()

  if (route.query.create === 'true' && canManageRequests.value) {
    openCreateForm()
    const query = { ...route.query }
    delete query.create
    await router.replace({ name: 'purchase-requests', query })
  }
})
onUnmounted(() => window.clearTimeout(successTimer))

async function loadData() {
  loading.value = true
  loadError.value = ''

  try {
    const [requestRecords, productRecords] = await Promise.all([
      purchaseRequestService.getAll(),
      productService.getAll(true),
    ])
    requests.value = requestRecords
    products.value = productRecords
    refreshSelectedRequest()
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load purchase requests.')
  } finally {
    loading.value = false
  }
}

function refreshSelectedRequest() {
  if (!selectedRequest.value) return
  selectedRequest.value =
    requests.value.find((request) => request.id === selectedRequest.value?.id) ?? null
}

function openCreateForm() {
  editingRequest.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(request: PurchaseRequest) {
  if (!canEditRequest(request)) return
  editingRequest.value = request
  selectedRequest.value = null
  formError.value = ''
  formOpen.value = true
}

function canEditRequest(request: PurchaseRequest) {
  return (
    request.workflow.currentStepCode === 'DRAFT' &&
    request.workflow.availableActions.some(
      (action) =>
        action.code === 'SUBMIT' && isWorkflowActionDirectlyAssignedToActor(action, actor.value),
    )
  )
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingRequest.value = null
  formError.value = ''
}

function openDetails(request: PurchaseRequest) {
  selectedRequest.value = request
  operationError.value = ''
}

function closeDetails() {
  if (deleting.value || actioning.value) return
  selectedRequest.value = null
  selectedAction.value = null
}

async function saveRequest(values: PurchaseRequestFormValues, submitAfterSave: boolean) {
  saving.value = true
  formError.value = ''
  const wasEditing = editingRequest.value !== null
  let savedRequest: PurchaseRequest | null = null

  try {
    if (editingRequest.value) {
      savedRequest = await purchaseRequestService.update(editingRequest.value.id, values)
    } else {
      savedRequest = await purchaseRequestService.create(values)
    }

    if (submitAfterSave) {
      savedRequest = await purchaseRequestService.executeAction(savedRequest.id, 'SUBMIT', {
        comment: wasEditing
          ? 'Updated and submitted from the form.'
          : 'Created and submitted from the form.',
      })
      showSuccess(`${savedRequest.requestNumber} was submitted for department review.`)
    } else {
      showSuccess(
        wasEditing
          ? `${savedRequest.requestNumber} was updated successfully.`
          : `${savedRequest.requestNumber} was created as a draft.`,
      )
    }

    formOpen.value = false
    editingRequest.value = null
    await loadData()
  } catch (error) {
    const message = getErrorMessage(error, 'Unable to save the purchase request.')

    if (savedRequest?.workflow.currentStepCode === 'DRAFT') {
      editingRequest.value = savedRequest
      await loadData()
      formError.value = `${savedRequest.requestNumber} was saved as a draft, but submission failed. ${message}`
    } else {
      formError.value = message
    }
  } finally {
    saving.value = false
  }
}

async function deleteDraft() {
  if (!selectedRequest.value) return
  const request = selectedRequest.value
  const confirmed = window.confirm(`Delete draft ${request.requestNumber}? This cannot be undone.`)
  if (!confirmed) return

  deleting.value = true
  operationError.value = ''

  try {
    await purchaseRequestService.delete(request.id)
    selectedRequest.value = null
    showSuccess(`${request.requestNumber} was deleted.`)
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to delete the draft.')
  } finally {
    deleting.value = false
  }
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
      {
        comment,
      },
    )
    selectedAction.value = null
    selectedRequest.value = null
    showSuccess(`${updated.requestNumber}: ${action.name} completed.`)
    await loadData()
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
  <section class="page-section purchase-request-page">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Procurement workflow</p>
        <h1>Purchase Requests</h1>
        <p class="page-description">
          Prepare purchasing drafts, route approvals, and review each request's workflow history.
        </p>
      </div>
      <button
        v-if="canManageRequests"
        class="button button-primary"
        type="button"
        @click="openCreateForm"
      >
        <span aria-hidden="true">+</span>
        New purchase request
      </button>
    </header>

    <div class="summary-grid purchase-summary-grid" aria-label="Purchase request summary">
      <article class="summary-card">
        <span class="summary-label">Drafts</span>
        <strong>{{ draftCount }}</strong>
        <span>Requests being prepared</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">In review</span>
        <strong>{{ reviewCount }}</strong>
        <span>Waiting for approval</span>
      </article>
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Completed</span>
        <strong>{{ completedCount }}</strong>
        <span>Approved or rejected</span>
      </article>
    </div>

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

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">&times;</button>
    </div>

    <section class="data-panel" aria-labelledby="purchase-request-list-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="purchase-request-list-title">Request register</h2>
          <p>{{ visibleRequests.length }} records shown</p>
        </div>

        <div class="toolbar-actions purchase-toolbar-actions">
          <label class="search-control">
            <span class="sr-only">Search purchase requests</span>
            <span aria-hidden="true">⌕</span>
            <input
              v-model="search"
              type="search"
              placeholder="Search number, requester, or department"
            />
          </label>
          <label class="step-filter-control">
            <span class="sr-only">Filter by workflow step</span>
            <select v-model="selectedStep">
              <option v-for="filter in stepFilters" :key="filter.value" :value="filter.value">
                {{ filter.label }}
              </option>
            </select>
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading purchase requests</strong>
        <p>Retrieving requests and workflow instances.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Purchase requests could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadData">Try again</button>
      </div>

      <div v-else-if="visibleRequests.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">RQ</div>
        <strong>{{
          search || selectedStep ? 'No matching requests' : 'No purchase requests yet'
        }}</strong>
        <p>
          {{
            search || selectedStep
              ? 'Change the search or workflow step filter.'
              : 'Create a draft to begin the purchasing workflow.'
          }}
        </p>
        <button
          v-if="canManageRequests && !search && !selectedStep"
          class="button button-primary"
          type="button"
          @click="openCreateForm"
        >
          Create purchase request
        </button>
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
              <th>Workflow step</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="request in visibleRequests" :key="request.id">
              <td>
                <div class="purchase-request-identity">
                  <span class="code-avatar">RQ</span>
                  <span>
                    <strong>{{ request.requestNumber }}</strong>
                    <small>{{ request.items.length }} items</small>
                  </span>
                </div>
              </td>
              <td>{{ request.requesterName || 'Not set' }}</td>
              <td>
                <div class="contact-stack">
                  <span>{{ request.departmentName || 'Not set' }}</span>
                  <small v-if="request.departmentCode">{{ request.departmentCode }}</small>
                </div>
              </td>
              <td>{{ formatDate(request.requiredDate) }}</td>
              <td>{{ formatCurrency(request.estimatedTotal) }}</td>
              <td>
                <span
                  class="workflow-step-badge"
                  :class="stepClass(request.workflow.currentStepCode)"
                >
                  {{ request.workflow.currentStepCode.replace(/_/g, ' ') }}
                </span>
              </td>
              <td>
                <div class="row-actions">
                  <button
                    v-if="!canEditRequest(request)"
                    class="text-button"
                    type="button"
                    @click="openDetails(request)"
                  >
                    Details
                  </button>
                  <button
                    v-if="canEditRequest(request)"
                    class="text-button"
                    type="button"
                    @click="openEditForm(request)"
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

    <PurchaseRequestForm
      v-if="formOpen"
      :purchase-request="editingRequest"
      :products="products"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveRequest"
    />

    <PurchaseRequestDetails
      v-if="selectedRequest"
      :purchase-request="selectedRequest"
      :actor="actor"
      :can-manage-draft="canEditRequest(selectedRequest)"
      :deleting="deleting"
      @close="closeDetails"
      @edit="openEditForm(selectedRequest)"
      @delete="deleteDraft"
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
