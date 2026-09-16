<script setup lang="ts">
import { ref, watch } from 'vue'

import type { PurchaseOrder } from '@/types/purchaseOrder'

const props = defineProps<{
  purchaseOrder: PurchaseOrder
  cancelling: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  close: []
  confirm: [reason: string]
}>()

const reason = ref('')
const validationError = ref('')

watch(
  () => props.purchaseOrder.id,
  () => {
    reason.value = ''
    validationError.value = ''
  },
  { immediate: true },
)

function submit() {
  const trimmedReason = reason.value.trim()
  validationError.value = ''

  if (!trimmedReason) {
    validationError.value = 'Cancellation reason is required.'
    return
  }
  if (trimmedReason.length > 500) {
    validationError.value = 'Cancellation reason cannot exceed 500 characters.'
    return
  }

  emit('confirm', trimmedReason)
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('close')">
    <section
      class="modal-card purchase-order-cancel-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="purchase-order-cancel-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Cancel issued order</p>
          <h2 id="purchase-order-cancel-title">{{ purchaseOrder.purchaseOrderNumber }}</h2>
        </div>
        <button
          class="icon-button"
          type="button"
          aria-label="Close cancellation"
          @click="emit('close')"
        >
          &times;
        </button>
      </header>

      <form class="purchase-order-cancel-form" novalidate @submit.prevent="submit">
        <div v-if="errorMessage" class="form-server-error" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Purchase order could not be cancelled</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <p class="purchase-order-cancel-warning">
          Cancelling an issued purchase order keeps it in the audit history and cannot be undone.
        </p>

        <div class="form-field">
          <div class="label-row">
            <label for="purchase-order-cancel-reason">Cancellation reason</label>
            <span>{{ reason.length }}/500</span>
          </div>
          <textarea
            id="purchase-order-cancel-reason"
            v-model="reason"
            maxlength="500"
            rows="4"
            placeholder="Explain why this purchase order is being cancelled"
            :aria-invalid="Boolean(validationError)"
          />
          <p v-if="validationError" class="field-error">{{ validationError }}</p>
        </div>

        <footer class="modal-actions">
          <button
            class="button button-secondary"
            type="button"
            :disabled="cancelling"
            @click="emit('close')"
          >
            Keep order
          </button>
          <button class="button button-danger" type="submit" :disabled="cancelling">
            {{ cancelling ? 'Cancelling...' : 'Cancel purchase order' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
