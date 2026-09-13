<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import QuotationComparison from '@/components/quotations/QuotationComparison.vue'
import QuotationDetails from '@/components/quotations/QuotationDetails.vue'
import QuotationForm from '@/components/quotations/QuotationForm.vue'
import AppToast from '@/components/ui/AppToast.vue'
import { useToast } from '@/composables/useToast'
import { purchaseRequestService } from '@/services/purchaseRequestService'
import { quotationService } from '@/services/quotationService'
import { supplierProductService } from '@/services/supplierProductService'
import { supplierService } from '@/services/supplierService'
import type { PurchaseRequest } from '@/types/purchaseRequest'
import type {
  Quotation,
  QuotationComparison as QuotationComparisonModel,
  QuotationFormValues,
  QuotationStatus,
} from '@/types/quotation'
import type { Supplier } from '@/types/supplier'
import type { SupplierProduct } from '@/types/supplierProduct'

const statusFilters: { value: '' | QuotationStatus; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Submitted', label: 'Submitted' },
  { value: 'Selected', label: 'Selected' },
  { value: 'NotSelected', label: 'Not selected' },
]

const quotations = ref<Quotation[]>([])
const approvedRequests = ref<PurchaseRequest[]>([])
const suppliers = ref<Supplier[]>([])
const supplierProducts = ref<SupplierProduct[]>([])
const loading = ref(true)
const saving = ref(false)
const busyQuotationId = ref<number | null>(null)
const selectingQuotationId = ref<number | null>(null)
const search = ref('')
const selectedStatus = ref<'' | QuotationStatus>('')
const selectedPurchaseRequestId = ref('all')
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const formOpen = ref(false)
const editingQuotation = ref<Quotation | null>(null)
const detailsQuotation = ref<Quotation | null>(null)
const comparison = ref<QuotationComparisonModel | null>(null)
const { toast, showSuccess, dismissToast } = useToast()

const draftCount = computed(
  () => quotations.value.filter((quotation) => quotation.status === 'Draft').length,
)
const submittedCount = computed(
  () => quotations.value.filter((quotation) => quotation.status === 'Submitted').length,
)
const selectedCount = computed(
  () => quotations.value.filter((quotation) => quotation.status === 'Selected').length,
)
const selectedRequestIds = computed(
  () =>
    new Set(
      quotations.value
        .filter((quotation) => quotation.status === 'Selected')
        .map((quotation) => quotation.purchaseRequestId),
    ),
)
const requestsAvailableForQuotation = computed(() =>
  approvedRequests.value.filter((request) => !selectedRequestIds.value.has(request.id)),
)
const purchaseRequestOptions = computed(() => {
  const records = new Map<number, string>()
  approvedRequests.value.forEach((request) => records.set(request.id, request.requestNumber))
  quotations.value.forEach((quotation) =>
    records.set(quotation.purchaseRequestId, quotation.purchaseRequestNumber),
  )
  return [...records.entries()].map(([id, number]) => ({ id, number }))
})
const visibleQuotations = computed(() => {
  const term = search.value.trim().toLowerCase()

  return quotations.value.filter((quotation) => {
    if (selectedStatus.value && quotation.status !== selectedStatus.value) return false
    if (
      selectedPurchaseRequestId.value !== 'all' &&
      quotation.purchaseRequestId !== Number(selectedPurchaseRequestId.value)
    ) {
      return false
    }
    if (!term) return true

    return [
      quotation.quotationNumber,
      quotation.purchaseRequestNumber,
      quotation.supplierCode,
      quotation.supplierName,
      quotation.supplierQuotationReference ?? '',
    ].some((value) => value.toLowerCase().includes(term))
  })
})

onMounted(loadData)
async function loadData() {
  loading.value = true
  loadError.value = ''

  try {
    const [quotationRecords, requestRecords, supplierRecords, relationshipRecords] =
      await Promise.all([
        quotationService.getAll(),
        purchaseRequestService.getAll('APPROVED'),
        supplierService.getAll(),
        supplierProductService.getAll(),
      ])

    quotations.value = quotationRecords
    approvedRequests.value = requestRecords
    suppliers.value = supplierRecords
    supplierProducts.value = relationshipRecords
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load supplier quotations.')
  } finally {
    loading.value = false
  }
}

function openCreateForm() {
  editingQuotation.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(quotation: Quotation) {
  editingQuotation.value = quotation
  detailsQuotation.value = null
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingQuotation.value = null
  formError.value = ''
}

async function saveQuotation(values: QuotationFormValues, submitAfterSave: boolean) {
  saving.value = true
  formError.value = ''

  try {
    let saved: Quotation
    if (editingQuotation.value) {
      saved = await quotationService.update(editingQuotation.value.id, {
        supplierQuotationReference: values.supplierQuotationReference,
        quotationDate: values.quotationDate,
        validUntil: values.validUntil,
        notes: values.notes,
        items: values.items,
      })
    } else {
      saved = await quotationService.create(values)
      editingQuotation.value = saved
    }

    if (submitAfterSave) saved = await quotationService.submit(saved.id)

    formOpen.value = false
    editingQuotation.value = null
    showSuccess(
      submitAfterSave
        ? `${saved.quotationNumber} was submitted for comparison.`
        : `${saved.quotationNumber} was saved as a draft.`,
    )
    await loadData()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the quotation.')
  } finally {
    saving.value = false
  }
}

async function submitQuotation(quotation: Quotation) {
  const confirmed = window.confirm(
    `Submit ${quotation.quotationNumber}? Submitted quotations can no longer be edited.`,
  )
  if (!confirmed) return

  busyQuotationId.value = quotation.id
  operationError.value = ''
  try {
    await quotationService.submit(quotation.id)
    showSuccess(`${quotation.quotationNumber} was submitted for comparison.`)
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to submit the quotation.')
  } finally {
    busyQuotationId.value = null
  }
}

async function deleteQuotation(quotation: Quotation) {
  const confirmed = window.confirm(
    `Delete draft ${quotation.quotationNumber} from ${quotation.supplierName}?`,
  )
  if (!confirmed) return

  busyQuotationId.value = quotation.id
  operationError.value = ''
  try {
    await quotationService.delete(quotation.id)
    showSuccess(`${quotation.quotationNumber} was deleted.`)
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to delete the quotation.')
  } finally {
    busyQuotationId.value = null
  }
}

async function openComparison(purchaseRequestId: number) {
  operationError.value = ''
  try {
    comparison.value = await quotationService.getComparison(purchaseRequestId)
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to load the quotation comparison.')
  }
}

async function selectWinner(quotationId: number) {
  const quotation = quotations.value.find((item) => item.id === quotationId)
  if (!quotation) return

  const confirmed = window.confirm(
    `Select ${quotation.supplierName} (${quotation.quotationNumber}) as the winning quotation? This cannot be changed in the current workflow.`,
  )
  if (!confirmed) return

  selectingQuotationId.value = quotationId
  operationError.value = ''
  try {
    await quotationService.select(quotationId)
    comparison.value = null
    showSuccess(`${quotation.quotationNumber} was selected as the winning quotation.`)
    await loadData()
  } catch (error) {
    operationError.value = getErrorMessage(error, 'Unable to select the winning quotation.')
  } finally {
    selectingQuotationId.value = null
  }
}

function statusLabel(status: QuotationStatus) {
  return status === 'NotSelected' ? 'Not selected' : status
}

function canEditQuotation(quotation: Quotation) {
  return quotation.status === 'Draft' && !selectedRequestIds.value.has(quotation.purchaseRequestId)
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat('en-MY', { style: 'currency', currency: 'MYR' }).format(value)
}

function formatDate(value: string | null) {
  if (!value) return 'No expiry'
  return new Intl.DateTimeFormat('en-MY', { dateStyle: 'medium' }).format(
    new Date(`${value}T00:00:00`),
  )
}
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Procurement sourcing</p>
        <h1>Supplier Quotations</h1>
        <p class="page-description">
          Record supplier prices for approved requests, compare offers, and select the winner.
        </p>
      </div>
      <button
        class="button button-primary"
        type="button"
        :disabled="loading || !requestsAvailableForQuotation.length"
        @click="openCreateForm"
      >
        <span aria-hidden="true">+</span>
        New quotation
      </button>
    </header>

    <div class="summary-grid" aria-label="Quotation summary">
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Drafts</span>
        <strong>{{ draftCount }}</strong>
        <span>Prices still being prepared</span>
      </article>
      <article class="summary-card">
        <span class="summary-label">Submitted</span>
        <strong>{{ submittedCount }}</strong>
        <span>Ready for supplier comparison</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Selected</span>
        <strong>{{ selectedCount }}</strong>
        <span>Winning quotations</span>
      </article>
    </div>

    <AppToast :toast="toast" @dismiss="dismissToast" />

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">&times;</button>
    </div>

    <section class="data-panel" aria-labelledby="quotation-list-title">
      <div class="panel-toolbar quotation-toolbar">
        <div>
          <h2 id="quotation-list-title">Quotation register</h2>
          <p>{{ visibleQuotations.length }} records shown</p>
        </div>

        <div class="toolbar-actions quotation-filters">
          <label class="search-control">
            <span class="sr-only">Search quotations</span>
            <span aria-hidden="true">⌕</span>
            <input
              v-model="search"
              type="search"
              placeholder="Search quotation, request, or supplier"
            />
          </label>

          <label class="relationship-filter-control">
            <span class="sr-only">Filter by purchase request</span>
            <select v-model="selectedPurchaseRequestId" aria-label="Filter by purchase request">
              <option value="all">All requests</option>
              <option
                v-for="request in purchaseRequestOptions"
                :key="request.id"
                :value="String(request.id)"
              >
                {{ request.number }}
              </option>
            </select>
          </label>

          <label class="relationship-filter-control">
            <span class="sr-only">Filter by status</span>
            <select v-model="selectedStatus" aria-label="Filter by quotation status">
              <option v-for="status in statusFilters" :key="status.value" :value="status.value">
                {{ status.label }}
              </option>
            </select>
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading supplier quotations</strong>
        <p>Retrieving approved requests and supplier prices.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Supplier quotations could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadData">Try again</button>
      </div>

      <div v-else-if="!visibleQuotations.length" class="panel-state">
        <div class="empty-icon" aria-hidden="true">QT</div>
        <strong>{{ quotations.length ? 'No matching quotations' : 'No quotations yet' }}</strong>
        <p v-if="!approvedRequests.length">
          Approve a purchase request before recording supplier quotations.
        </p>
        <p v-else>Create a quotation from an approved request and an eligible supplier.</p>
        <button
          v-if="requestsAvailableForQuotation.length"
          class="button button-primary"
          type="button"
          @click="openCreateForm"
        >
          Create quotation
        </button>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Quotation</th>
              <th>Purchase request</th>
              <th>Supplier</th>
              <th>Valid until</th>
              <th>Total</th>
              <th>Status</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="quotation in visibleQuotations" :key="quotation.id">
              <td>
                <div class="quotation-identity">
                  <span class="code-avatar">QT</span>
                  <span>
                    <strong>{{ quotation.quotationNumber }}</strong>
                    <small>{{
                      quotation.supplierQuotationReference || 'No supplier reference'
                    }}</small>
                  </span>
                </div>
              </td>
              <td>
                <span class="table-primary">{{ quotation.purchaseRequestNumber }}</span>
                <small class="table-secondary">{{ quotation.items.length }} items</small>
              </td>
              <td>
                <span class="table-primary">{{ quotation.supplierName }}</span>
                <small class="table-secondary">{{ quotation.supplierCode }}</small>
              </td>
              <td>{{ formatDate(quotation.validUntil) }}</td>
              <td>
                <strong>{{ formatCurrency(quotation.totalAmount) }}</strong>
              </td>
              <td>
                <span class="quotation-status" :class="`status-${quotation.status.toLowerCase()}`">
                  {{ statusLabel(quotation.status) }}
                </span>
              </td>
              <td>
                <div class="row-actions quotation-row-actions">
                  <button class="text-button" type="button" @click="detailsQuotation = quotation">
                    View
                  </button>
                  <button
                    v-if="canEditQuotation(quotation)"
                    class="text-button"
                    type="button"
                    :disabled="busyQuotationId === quotation.id"
                    @click="openEditForm(quotation)"
                  >
                    Edit
                  </button>
                  <button
                    v-if="canEditQuotation(quotation)"
                    class="text-button text-button-positive"
                    type="button"
                    :disabled="busyQuotationId === quotation.id"
                    @click="submitQuotation(quotation)"
                  >
                    Submit
                  </button>
                  <button
                    v-if="quotation.status !== 'Draft'"
                    class="text-button"
                    type="button"
                    @click="openComparison(quotation.purchaseRequestId)"
                  >
                    Compare
                  </button>
                  <button
                    v-if="quotation.status === 'Draft'"
                    class="text-button text-button-danger"
                    type="button"
                    :disabled="busyQuotationId === quotation.id"
                    @click="deleteQuotation(quotation)"
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

    <QuotationForm
      v-if="formOpen"
      :quotation="editingQuotation"
      :purchase-requests="requestsAvailableForQuotation"
      :suppliers="suppliers"
      :supplier-products="supplierProducts"
      :existing-quotations="quotations"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveQuotation"
    />

    <QuotationDetails
      v-if="detailsQuotation"
      :quotation="detailsQuotation"
      @close="detailsQuotation = null"
    />

    <QuotationComparison
      v-if="comparison"
      :comparison="comparison"
      :selecting-quotation-id="selectingQuotationId"
      @close="comparison = null"
      @select="selectWinner"
    />
  </section>
</template>
