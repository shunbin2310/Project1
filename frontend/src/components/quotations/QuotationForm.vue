<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { PurchaseRequest } from '@/types/purchaseRequest'
import type { Quotation, QuotationFormValues } from '@/types/quotation'
import type { Supplier } from '@/types/supplier'
import type { SupplierProduct } from '@/types/supplierProduct'

const props = defineProps<{
  quotation: Quotation | null
  purchaseRequests: PurchaseRequest[]
  suppliers: Supplier[]
  supplierProducts: SupplierProduct[]
  existingQuotations: Quotation[]
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: QuotationFormValues, submitAfterSave: boolean]
}>()

type FormState = QuotationFormValues

const form = reactive<FormState>({
  purchaseRequestId: 0,
  supplierId: 0,
  supplierQuotationReference: null,
  quotationDate: todayValue(),
  validUntil: null,
  notes: null,
  items: [],
})

const errors = reactive({
  purchaseRequestId: '',
  supplierId: '',
  quotationDate: '',
  validUntil: '',
  supplierQuotationReference: '',
  notes: '',
  items: '',
})

const isEditing = computed(() => props.quotation !== null)
const title = computed(() =>
  props.quotation ? `Edit ${props.quotation.quotationNumber}` : 'Create supplier quotation',
)
const selectedPurchaseRequest = computed(() =>
  props.purchaseRequests.find((request) => request.id === form.purchaseRequestId),
)
const selectedSupplier = computed(() =>
  props.suppliers.find((supplier) => supplier.id === form.supplierId),
)
const eligibleSuppliers = computed(() => {
  const request = selectedPurchaseRequest.value
  if (!request) return []

  const productIds = [...new Set(request.items.map((item) => item.productId))]
  const quotedSupplierIds = new Set(
    props.existingQuotations
      .filter(
        (quotation) =>
          quotation.purchaseRequestId === request.id && quotation.id !== props.quotation?.id,
      )
      .map((quotation) => quotation.supplierId),
  )

  return props.suppliers.filter(
    (supplier) =>
      supplier.isActive &&
      !quotedSupplierIds.has(supplier.id) &&
      productIds.every((productId) =>
        props.supplierProducts.some(
          (relationship) =>
            relationship.supplierId === supplier.id &&
            relationship.productId === productId &&
            relationship.isActive,
        ),
      ),
  )
})
const quoteRows = computed(() =>
  (selectedPurchaseRequest.value?.items ?? []).map((requestItem) => ({
    requestItem,
    quoteItem: form.items.find((item) => item.purchaseRequestItemId === requestItem.id),
  })),
)
const totalAmount = computed(() =>
  quoteRows.value.reduce(
    (total, row) => total + row.requestItem.quantity * (row.quoteItem?.unitPrice ?? 0),
    0,
  ),
)

watch([() => props.quotation, () => props.purchaseRequests], () => initializeForm(), {
  immediate: true,
})

function initializeForm() {
  clearErrors()

  if (props.quotation) {
    form.purchaseRequestId = props.quotation.purchaseRequestId
    form.supplierId = props.quotation.supplierId
    form.supplierQuotationReference = props.quotation.supplierQuotationReference
    form.quotationDate = props.quotation.quotationDate
    form.validUntil = props.quotation.validUntil
    form.notes = props.quotation.notes
    form.items = props.quotation.items.map((item) => ({
      purchaseRequestItemId: item.purchaseRequestItemId,
      unitPrice: item.unitPrice,
    }))
    return
  }

  form.purchaseRequestId = props.purchaseRequests[0]?.id ?? 0
  form.supplierId = 0
  form.supplierQuotationReference = null
  form.quotationDate = todayValue()
  form.validUntil = null
  form.notes = null
  copyRequestItems()
}

function purchaseRequestChanged() {
  form.supplierId = 0
  copyRequestItems()
  errors.purchaseRequestId = ''
  errors.supplierId = ''
  errors.items = ''
}

function copyRequestItems() {
  form.items = (selectedPurchaseRequest.value?.items ?? []).map((item) => ({
    purchaseRequestItemId: item.id,
    unitPrice: 0,
  }))
}

function clearErrors() {
  Object.keys(errors).forEach((key) => {
    errors[key as keyof typeof errors] = ''
  })
}

function validate(submitAfterSave: boolean) {
  clearErrors()

  if (!selectedPurchaseRequest.value) {
    errors.purchaseRequestId = 'Select an approved purchase request.'
  }
  if (!selectedSupplier.value) {
    errors.supplierId = 'Select an eligible supplier.'
  }
  if (!form.quotationDate) {
    errors.quotationDate = 'Quotation date is required.'
  }
  if (form.validUntil && form.quotationDate && form.validUntil < form.quotationDate) {
    errors.validUntil = 'Valid until must be on or after the quotation date.'
  }
  if ((form.supplierQuotationReference?.length ?? 0) > 100) {
    errors.supplierQuotationReference = 'Supplier reference cannot exceed 100 characters.'
  }
  if ((form.notes?.length ?? 0) > 1000) {
    errors.notes = 'Notes cannot exceed 1000 characters.'
  }
  if (form.items.length === 0) {
    errors.items = 'The purchase request must contain at least one item.'
  } else if (
    form.items.some(
      (item) =>
        !Number.isFinite(item.unitPrice) ||
        (submitAfterSave ? item.unitPrice <= 0 : item.unitPrice < 0),
    )
  ) {
    errors.items = submitAfterSave
      ? 'Enter a unit price greater than zero for every item before submission.'
      : 'Unit prices cannot be negative.'
  }

  return !Object.values(errors).some(Boolean)
}

function submitForm(submitAfterSave: boolean) {
  if (!validate(submitAfterSave)) return

  emit(
    'save',
    {
      purchaseRequestId: form.purchaseRequestId,
      supplierId: form.supplierId,
      supplierQuotationReference: form.supplierQuotationReference?.trim() || null,
      quotationDate: form.quotationDate,
      validUntil: form.validUntil || null,
      notes: form.notes?.trim() || null,
      items: form.items.map((item) => ({ ...item })),
    },
    submitAfterSave,
  )
}

function todayValue() {
  const today = new Date()
  const offset = today.getTimezoneOffset() * 60_000
  return new Date(today.getTime() - offset).toISOString().slice(0, 10)
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat('en-MY', { style: 'currency', currency: 'MYR' }).format(value)
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('en-MY', { maximumFractionDigits: 3 }).format(value)
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('cancel')">
    <section
      class="modal-card quotation-form-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="quotation-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Supplier quotation</p>
          <h2 id="quotation-form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          &times;
        </button>
      </header>

      <form class="quotation-form" novalidate @submit.prevent="submitForm(false)">
        <div v-if="errorMessage" class="form-server-error form-grid-full" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Quotation could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div class="form-field">
          <label for="quotation-request">Approved purchase request</label>
          <select
            v-if="!isEditing"
            id="quotation-request"
            v-model.number="form.purchaseRequestId"
            :aria-invalid="Boolean(errors.purchaseRequestId)"
            @change="purchaseRequestChanged"
          >
            <option :value="0" disabled>Select a purchase request</option>
            <option v-for="request in purchaseRequests" :key="request.id" :value="request.id">
              {{ request.requestNumber }} - {{ request.departmentName || 'No department' }}
            </option>
          </select>
          <input v-else id="quotation-request" :value="quotation?.purchaseRequestNumber" readonly />
          <p v-if="errors.purchaseRequestId" class="field-error">
            {{ errors.purchaseRequestId }}
          </p>
        </div>

        <div class="form-field">
          <label for="quotation-supplier">Eligible supplier</label>
          <select
            v-if="!isEditing"
            id="quotation-supplier"
            v-model.number="form.supplierId"
            :aria-invalid="Boolean(errors.supplierId)"
          >
            <option :value="0" disabled>Select a supplier</option>
            <option v-for="supplier in eligibleSuppliers" :key="supplier.id" :value="supplier.id">
              {{ supplier.code }} - {{ supplier.name }}
            </option>
          </select>
          <input
            v-else
            id="quotation-supplier"
            :value="`${quotation?.supplierCode} - ${quotation?.supplierName}`"
            readonly
          />
          <p v-if="errors.supplierId" class="field-error">{{ errors.supplierId }}</p>
          <p
            v-else-if="!isEditing && selectedPurchaseRequest && !eligibleSuppliers.length"
            class="field-hint"
          >
            No unused supplier can supply every item in this request.
          </p>
          <p v-else class="field-hint">
            Eligibility comes from active Supplier Products relationships.
          </p>
        </div>

        <div class="form-field">
          <label for="quotation-reference">Supplier reference</label>
          <input
            id="quotation-reference"
            v-model="form.supplierQuotationReference"
            maxlength="100"
            placeholder="e.g. SUP-Q-2026-001"
            :aria-invalid="Boolean(errors.supplierQuotationReference)"
          />
          <p v-if="errors.supplierQuotationReference" class="field-error">
            {{ errors.supplierQuotationReference }}
          </p>
        </div>

        <div class="quotation-date-grid form-grid-full">
          <div class="form-field">
            <label for="quotation-date">Quotation date</label>
            <input
              id="quotation-date"
              v-model="form.quotationDate"
              type="date"
              :aria-invalid="Boolean(errors.quotationDate)"
            />
            <p v-if="errors.quotationDate" class="field-error">{{ errors.quotationDate }}</p>
          </div>
          <div class="form-field">
            <label for="quotation-valid-until">Valid until</label>
            <input
              id="quotation-valid-until"
              v-model="form.validUntil"
              type="date"
              :min="form.quotationDate"
              :aria-invalid="Boolean(errors.validUntil)"
            />
            <p v-if="errors.validUntil" class="field-error">{{ errors.validUntil }}</p>
          </div>
        </div>

        <div class="form-field form-grid-full">
          <div class="label-row">
            <label for="quotation-notes">Notes</label>
            <span>{{ form.notes?.length ?? 0 }}/1000</span>
          </div>
          <textarea
            id="quotation-notes"
            v-model="form.notes"
            maxlength="1000"
            rows="3"
            placeholder="Payment terms, delivery details, or other supplier notes"
            :aria-invalid="Boolean(errors.notes)"
          />
          <p v-if="errors.notes" class="field-error">{{ errors.notes }}</p>
        </div>

        <section class="quotation-items form-grid-full" aria-labelledby="quotation-items-title">
          <div class="request-items-heading">
            <div>
              <h3 id="quotation-items-title">Quoted items</h3>
              <p>Products and quantities are copied from the approved purchase request.</p>
            </div>
          </div>

          <p v-if="errors.items" class="field-error quotation-items-error" role="alert">
            {{ errors.items }}
          </p>

          <div v-if="!quoteRows.length" class="request-items-empty">
            Select an approved purchase request to load its items.
          </div>

          <div v-else class="table-scroll quotation-entry-table">
            <table>
              <thead>
                <tr>
                  <th>Product</th>
                  <th>Quantity</th>
                  <th>Estimated price</th>
                  <th>Supplier unit price</th>
                  <th>Line total</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in quoteRows" :key="row.requestItem.id">
                  <td>
                    <strong>{{ row.requestItem.productName }}</strong>
                    <small>{{ row.requestItem.productCode }}</small>
                  </td>
                  <td>
                    {{ formatQuantity(row.requestItem.quantity) }}
                    {{ row.requestItem.unitOfMeasureCode }}
                  </td>
                  <td>{{ formatCurrency(row.requestItem.estimatedUnitPrice) }}</td>
                  <td>
                    <label class="sr-only" :for="`quotation-price-${row.requestItem.id}`">
                      Unit price for {{ row.requestItem.productName }}
                    </label>
                    <input
                      v-if="row.quoteItem"
                      :id="`quotation-price-${row.requestItem.id}`"
                      v-model.number="row.quoteItem.unitPrice"
                      class="table-price-input"
                      type="number"
                      min="0"
                      step="0.01"
                    />
                  </td>
                  <td>
                    <strong>
                      {{
                        formatCurrency(row.requestItem.quantity * (row.quoteItem?.unitPrice ?? 0))
                      }}
                    </strong>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="request-total quotation-total">
            <span>Quotation total</span>
            <strong>{{ formatCurrency(totalAmount) }}</strong>
          </div>
        </section>

        <footer class="modal-actions form-grid-full">
          <button
            class="button button-secondary"
            type="button"
            :disabled="saving"
            @click="emit('cancel')"
          >
            Cancel
          </button>
          <button class="button button-secondary" type="submit" :disabled="saving">
            {{ saving ? 'Saving...' : 'Save draft' }}
          </button>
          <button
            class="button button-primary"
            type="button"
            :disabled="saving"
            @click="submitForm(true)"
          >
            {{ saving ? 'Processing...' : 'Save and submit' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
