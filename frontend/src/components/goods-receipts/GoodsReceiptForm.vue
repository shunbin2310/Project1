<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type {
  GoodsReceipt,
  GoodsReceiptFormValues,
  GoodsReceiptItemInput,
  ReceivablePurchaseOrder,
} from '@/types/goodsReceipt'

const props = defineProps<{
  goodsReceipt: GoodsReceipt | null
  purchaseOrders: ReceivablePurchaseOrder[]
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: GoodsReceiptFormValues]
}>()

const form = reactive<GoodsReceiptFormValues>({
  purchaseOrderId: 0,
  supplierDeliveryNoteNumber: null,
  receivedDate: todayValue(),
  notes: null,
  items: [],
})

const errors = reactive({
  purchaseOrderId: '',
  supplierDeliveryNoteNumber: '',
  receivedDate: '',
  notes: '',
  items: '',
})

const isEditing = computed(() => props.goodsReceipt !== null)
const selectedPurchaseOrder = computed(() =>
  props.purchaseOrders.find((order) => order.id === form.purchaseOrderId),
)
const receiptRows = computed(() =>
  (selectedPurchaseOrder.value?.items ?? []).map((orderItem) => ({
    orderItem,
    receiptItem: form.items.find((item) => item.purchaseOrderItemId === orderItem.id),
  })),
)
const receivedNowTotal = computed(() =>
  form.items.reduce((total, item) => total + (Number(item.quantityReceived) || 0), 0),
)

watch([() => props.goodsReceipt, () => props.purchaseOrders], initializeForm, {
  immediate: true,
})

function initializeForm() {
  clearErrors()

  if (props.goodsReceipt) {
    form.purchaseOrderId = props.goodsReceipt.purchaseOrderId
    form.supplierDeliveryNoteNumber = props.goodsReceipt.supplierDeliveryNoteNumber
    form.receivedDate = props.goodsReceipt.receivedDate
    form.notes = props.goodsReceipt.notes
    form.items = selectedPurchaseOrder.value
      ? selectedPurchaseOrder.value.items.map((item) => ({
          purchaseOrderItemId: item.id,
          quantityReceived:
            props.goodsReceipt?.items.find(
              (receiptItem) => receiptItem.purchaseOrderItemId === item.id,
            )?.quantityReceived ?? 0,
        }))
      : props.goodsReceipt.items.map((item) => ({
          purchaseOrderItemId: item.purchaseOrderItemId,
          quantityReceived: item.quantityReceived,
        }))
    return
  }

  form.purchaseOrderId = props.purchaseOrders[0]?.id ?? 0
  form.supplierDeliveryNoteNumber = null
  form.receivedDate = todayValue()
  form.notes = null
  copyPurchaseOrderItems()
}

function purchaseOrderChanged() {
  errors.purchaseOrderId = ''
  errors.receivedDate = ''
  errors.items = ''
  copyPurchaseOrderItems()
}

function copyPurchaseOrderItems() {
  form.items = (selectedPurchaseOrder.value?.items ?? []).map((item) => ({
    purchaseOrderItemId: item.id,
    quantityReceived: 0,
  }))
}

function clearErrors() {
  Object.keys(errors).forEach((key) => {
    errors[key as keyof typeof errors] = ''
  })
}

function validate() {
  clearErrors()
  const purchaseOrder = selectedPurchaseOrder.value

  if (!purchaseOrder) errors.purchaseOrderId = 'Select an open purchase order.'
  if (!form.receivedDate) {
    errors.receivedDate = 'Received date is required.'
  } else if (form.receivedDate > todayValue()) {
    errors.receivedDate = 'Received date cannot be in the future.'
  } else if (purchaseOrder && form.receivedDate < purchaseOrder.orderDate) {
    errors.receivedDate = 'Received date cannot be before the purchase order date.'
  }
  if ((form.supplierDeliveryNoteNumber?.length ?? 0) > 100) {
    errors.supplierDeliveryNoteNumber = 'Delivery note number cannot exceed 100 characters.'
  }
  if ((form.notes?.length ?? 0) > 1000) {
    errors.notes = 'Notes cannot exceed 1000 characters.'
  }

  const positiveItems = form.items.filter((item) => Number(item.quantityReceived) > 0)
  if (!positiveItems.length) {
    errors.items = 'Enter a received quantity for at least one item.'
  } else if (
    positiveItems.some((item) => {
      const orderItem = purchaseOrder?.items.find(
        (candidate) => candidate.id === item.purchaseOrderItemId,
      )
      return (
        !Number.isFinite(Number(item.quantityReceived)) ||
        !orderItem ||
        Number(item.quantityReceived) > orderItem.remainingQuantity
      )
    })
  ) {
    errors.items = 'Received quantity cannot exceed the remaining purchase order quantity.'
  }

  return !Object.values(errors).some(Boolean)
}

function submitForm() {
  if (!validate()) return

  const items: GoodsReceiptItemInput[] = form.items
    .filter((item) => Number(item.quantityReceived) > 0)
    .map((item) => ({
      purchaseOrderItemId: item.purchaseOrderItemId,
      quantityReceived: Number(item.quantityReceived),
    }))

  emit('save', {
    purchaseOrderId: form.purchaseOrderId,
    supplierDeliveryNoteNumber: form.supplierDeliveryNoteNumber?.trim() || null,
    receivedDate: form.receivedDate,
    notes: form.notes?.trim() || null,
    items,
  })
}

function todayValue() {
  const today = new Date()
  const offset = today.getTimezoneOffset() * 60_000
  return new Date(today.getTime() - offset).toISOString().slice(0, 10)
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('en-MY', { maximumFractionDigits: 3 }).format(value)
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('cancel')">
    <section
      class="modal-card goods-receipt-form-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="goods-receipt-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Goods receiving</p>
          <h2 id="goods-receipt-form-title">
            {{ goodsReceipt ? `Edit ${goodsReceipt.goodsReceiptNumber}` : 'Create goods receipt' }}
          </h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          &times;
        </button>
      </header>

      <form class="goods-receipt-form" novalidate @submit.prevent="submitForm">
        <div v-if="errorMessage" class="form-server-error form-grid-full" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Goods receipt could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div class="form-field">
          <label for="goods-receipt-order">Open purchase order</label>
          <select
            v-if="!isEditing"
            id="goods-receipt-order"
            v-model.number="form.purchaseOrderId"
            :aria-invalid="Boolean(errors.purchaseOrderId)"
            @change="purchaseOrderChanged"
          >
            <option :value="0" disabled>Select a purchase order</option>
            <option v-for="order in purchaseOrders" :key="order.id" :value="order.id">
              {{ order.purchaseOrderNumber }} · {{ order.supplierName }}
            </option>
          </select>
          <input
            v-else
            id="goods-receipt-order"
            :value="`${goodsReceipt?.purchaseOrderNumber} · ${goodsReceipt?.supplierName}`"
            readonly
          />
          <p v-if="errors.purchaseOrderId" class="field-error">{{ errors.purchaseOrderId }}</p>
          <p v-else class="field-hint">
            Only issued or partially received purchase orders are available.
          </p>
        </div>

        <div class="form-field">
          <label for="goods-receipt-date">Received date</label>
          <input
            id="goods-receipt-date"
            v-model="form.receivedDate"
            type="date"
            :max="todayValue()"
            :min="selectedPurchaseOrder?.orderDate"
            :aria-invalid="Boolean(errors.receivedDate)"
          />
          <p v-if="errors.receivedDate" class="field-error">{{ errors.receivedDate }}</p>
        </div>

        <div class="form-field form-grid-full">
          <label for="goods-receipt-delivery-note">Supplier delivery note number</label>
          <input
            id="goods-receipt-delivery-note"
            v-model="form.supplierDeliveryNoteNumber"
            maxlength="100"
            placeholder="e.g. DN-2026-001"
            :aria-invalid="Boolean(errors.supplierDeliveryNoteNumber)"
          />
          <p v-if="errors.supplierDeliveryNoteNumber" class="field-error">
            {{ errors.supplierDeliveryNoteNumber }}
          </p>
          <p v-else class="field-hint">
            Optional, but duplicate references are not allowed per PO.
          </p>
        </div>

        <div class="form-field form-grid-full">
          <div class="label-row">
            <label for="goods-receipt-notes">Receiving notes</label>
            <span>{{ form.notes?.length ?? 0 }}/1000</span>
          </div>
          <textarea
            id="goods-receipt-notes"
            v-model="form.notes"
            maxlength="1000"
            rows="3"
            placeholder="Condition, damaged packaging, or receiving remarks"
            :aria-invalid="Boolean(errors.notes)"
          />
          <p v-if="errors.notes" class="field-error">{{ errors.notes }}</p>
        </div>

        <section
          class="goods-receipt-items form-grid-full"
          aria-labelledby="goods-receipt-items-title"
        >
          <div class="request-items-heading">
            <div>
              <h3 id="goods-receipt-items-title">Received items</h3>
              <p>Enter only the quantities physically received in this delivery.</p>
            </div>
          </div>

          <p v-if="errors.items" class="field-error goods-receipt-items-error" role="alert">
            {{ errors.items }}
          </p>

          <div v-if="!receiptRows.length" class="request-items-empty">
            Select an open purchase order to load its remaining items.
          </div>

          <div v-else class="table-scroll goods-receipt-entry-table">
            <table>
              <thead>
                <tr>
                  <th>Product</th>
                  <th>Ordered</th>
                  <th>Previously received</th>
                  <th>Remaining</th>
                  <th>Receive now</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in receiptRows" :key="row.orderItem.id">
                  <td>
                    <strong>{{ row.orderItem.productName }}</strong>
                    <small>{{ row.orderItem.productCode }}</small>
                  </td>
                  <td>
                    {{ formatQuantity(row.orderItem.quantity) }}
                    {{ row.orderItem.unitOfMeasureCode }}
                  </td>
                  <td>{{ formatQuantity(row.orderItem.previouslyReceivedQuantity) }}</td>
                  <td>
                    <strong>{{ formatQuantity(row.orderItem.remainingQuantity) }}</strong>
                  </td>
                  <td>
                    <label class="sr-only" :for="`goods-receipt-quantity-${row.orderItem.id}`">
                      Receive now for {{ row.orderItem.productName }}
                    </label>
                    <input
                      v-if="row.receiptItem"
                      :id="`goods-receipt-quantity-${row.orderItem.id}`"
                      v-model.number="row.receiptItem.quantityReceived"
                      class="table-quantity-input"
                      type="number"
                      min="0"
                      :max="row.orderItem.remainingQuantity"
                      step="0.001"
                      :disabled="row.orderItem.remainingQuantity <= 0"
                    />
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="request-total goods-receipt-total">
            <span>Total quantity in this receipt</span>
            <strong>{{ formatQuantity(receivedNowTotal) }}</strong>
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
          <button class="button button-primary" type="submit" :disabled="saving">
            {{ saving ? 'Saving...' : goodsReceipt ? 'Save changes' : 'Create draft' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
