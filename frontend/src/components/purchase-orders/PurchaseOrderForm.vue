<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { PurchaseOrder, PurchaseOrderFormValues } from '@/types/purchaseOrder'
import type { Quotation } from '@/types/quotation'

const props = defineProps<{
  purchaseOrder: PurchaseOrder | null
  quotations: Quotation[]
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: PurchaseOrderFormValues, submitAfterSave: boolean]
}>()

const form = reactive<PurchaseOrderFormValues>({
  quotationId: 0,
  orderDate: todayValue(),
  expectedDeliveryDate: null,
  deliveryAddress: null,
  notes: null,
})

const errors = reactive({
  quotationId: '',
  orderDate: '',
  expectedDeliveryDate: '',
  deliveryAddress: '',
  notes: '',
})

const isEditing = computed(() => props.purchaseOrder !== null)
const selectedQuotation = computed(() =>
  props.quotations.find((quotation) => quotation.id === form.quotationId),
)
const displayItems = computed(
  () => props.purchaseOrder?.items ?? selectedQuotation.value?.items ?? [],
)
const totalAmount = computed(
  () => props.purchaseOrder?.totalAmount ?? selectedQuotation.value?.totalAmount ?? 0,
)

watch([() => props.purchaseOrder, () => props.quotations], initializeForm, { immediate: true })

function initializeForm() {
  clearErrors()

  if (props.purchaseOrder) {
    form.quotationId = props.purchaseOrder.quotationId
    form.orderDate = props.purchaseOrder.orderDate
    form.expectedDeliveryDate = props.purchaseOrder.expectedDeliveryDate
    form.deliveryAddress = props.purchaseOrder.deliveryAddress
    form.notes = props.purchaseOrder.notes
    return
  }

  form.quotationId = props.quotations[0]?.id ?? 0
  form.orderDate = todayValue()
  form.expectedDeliveryDate = null
  form.deliveryAddress = null
  form.notes = null
}

function clearErrors() {
  Object.keys(errors).forEach((key) => {
    errors[key as keyof typeof errors] = ''
  })
}

function validate(submitAfterSave: boolean) {
  clearErrors()

  if (!isEditing.value && !selectedQuotation.value) {
    errors.quotationId = 'Select a winning quotation.'
  }
  if (!form.orderDate) errors.orderDate = 'Order date is required.'
  if (form.expectedDeliveryDate && form.orderDate && form.expectedDeliveryDate < form.orderDate) {
    errors.expectedDeliveryDate = 'Expected delivery must be on or after the order date.'
  }
  if (submitAfterSave && !form.expectedDeliveryDate) {
    errors.expectedDeliveryDate = 'Expected delivery date is required before submission.'
  }
  if ((form.deliveryAddress?.length ?? 0) > 500) {
    errors.deliveryAddress = 'Delivery address cannot exceed 500 characters.'
  }
  if (submitAfterSave && !form.deliveryAddress?.trim()) {
    errors.deliveryAddress = 'Delivery address is required before submission.'
  }
  if ((form.notes?.length ?? 0) > 1000) {
    errors.notes = 'Notes cannot exceed 1000 characters.'
  }

  return !Object.values(errors).some(Boolean)
}

function submitForm(submitAfterSave = false) {
  if (!validate(submitAfterSave)) return

  emit('save', {
    quotationId: form.quotationId,
    orderDate: form.orderDate,
    expectedDeliveryDate: form.expectedDeliveryDate || null,
    deliveryAddress: form.deliveryAddress?.trim() || null,
    notes: form.notes?.trim() || null,
  }, submitAfterSave)
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
      class="modal-card purchase-order-form-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="purchase-order-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Purchase order</p>
          <h2 id="purchase-order-form-title">
            {{
              purchaseOrder ? `Edit ${purchaseOrder.purchaseOrderNumber}` : 'Create purchase order'
            }}
          </h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          &times;
        </button>
      </header>

      <form class="purchase-order-form" novalidate @submit.prevent="submitForm(false)">
        <div v-if="errorMessage" class="form-server-error form-grid-full" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Purchase order could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div class="form-field form-grid-full">
          <label for="purchase-order-quotation">Selected supplier quotation</label>
          <select
            v-if="!isEditing"
            id="purchase-order-quotation"
            v-model.number="form.quotationId"
            :aria-invalid="Boolean(errors.quotationId)"
          >
            <option :value="0" disabled>Select a winning quotation</option>
            <option v-for="quotation in quotations" :key="quotation.id" :value="quotation.id">
              {{ quotation.quotationNumber }} · {{ quotation.supplierName }} ·
              {{ quotation.purchaseRequestNumber }}
            </option>
          </select>
          <input
            v-else
            id="purchase-order-quotation"
            :value="`${purchaseOrder?.quotationNumber} · ${purchaseOrder?.supplierName}`"
            readonly
          />
          <p v-if="errors.quotationId" class="field-error">{{ errors.quotationId }}</p>
          <p v-else class="field-hint">
            Supplier, products, quantities, and prices are copied as an order snapshot.
          </p>
        </div>

        <div class="form-field">
          <label for="purchase-order-date">Order date</label>
          <input
            id="purchase-order-date"
            v-model="form.orderDate"
            type="date"
            :aria-invalid="Boolean(errors.orderDate)"
          />
          <p v-if="errors.orderDate" class="field-error">{{ errors.orderDate }}</p>
        </div>

        <div class="form-field">
          <label for="purchase-order-delivery-date">Expected delivery date</label>
          <input
            id="purchase-order-delivery-date"
            v-model="form.expectedDeliveryDate"
            type="date"
            :min="form.orderDate"
            :aria-invalid="Boolean(errors.expectedDeliveryDate)"
          />
          <p v-if="errors.expectedDeliveryDate" class="field-error">
            {{ errors.expectedDeliveryDate }}
          </p>
          <p v-else class="field-hint">Required before the purchase order can be submitted.</p>
        </div>

        <div class="form-field form-grid-full">
          <div class="label-row">
            <label for="purchase-order-address">Delivery address</label>
            <span>{{ form.deliveryAddress?.length ?? 0 }}/500</span>
          </div>
          <textarea
            id="purchase-order-address"
            v-model="form.deliveryAddress"
            maxlength="500"
            rows="2"
            placeholder="Warehouse or office delivery address"
            :aria-invalid="Boolean(errors.deliveryAddress)"
          />
          <p v-if="errors.deliveryAddress" class="field-error">{{ errors.deliveryAddress }}</p>
          <p v-else class="field-hint">Required before the purchase order can be submitted.</p>
        </div>

        <div class="form-field form-grid-full">
          <div class="label-row">
            <label for="purchase-order-notes">Order notes</label>
            <span>{{ form.notes?.length ?? 0 }}/1000</span>
          </div>
          <textarea
            id="purchase-order-notes"
            v-model="form.notes"
            maxlength="1000"
            rows="3"
            placeholder="Delivery instructions, payment terms, or internal notes"
            :aria-invalid="Boolean(errors.notes)"
          />
          <p v-if="errors.notes" class="field-error">{{ errors.notes }}</p>
        </div>

        <section
          class="quotation-items form-grid-full"
          aria-labelledby="purchase-order-items-title"
        >
          <div class="request-items-heading">
            <div>
              <h3 id="purchase-order-items-title">Ordered items</h3>
              <p>Read-only prices copied from the selected quotation.</p>
            </div>
          </div>

          <div v-if="!displayItems.length" class="request-items-empty">
            Select a winning quotation to preview its items.
          </div>

          <div v-else class="table-scroll quotation-entry-table">
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
                <tr v-for="item in displayItems" :key="item.id">
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

          <div class="request-total quotation-total">
            <span>Purchase order total</span>
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
            {{ saving ? 'Saving...' : purchaseOrder ? 'Save changes' : 'Create draft' }}
          </button>
          <button
            class="button button-primary"
            type="button"
            :disabled="saving"
            @click="submitForm(true)"
          >
            {{ saving ? 'Saving...' : purchaseOrder ? 'Save and submit' : 'Create and submit' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
