<script setup lang="ts">
import type { PurchaseOrderEmailPreview } from '@/types/purchaseOrder'

defineProps<{
  preview: PurchaseOrderEmailPreview
}>()

const emit = defineEmits<{
  close: []
}>()
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('close')">
    <section
      class="modal-card purchase-order-email-preview-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="purchase-order-email-preview-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Email preview</p>
          <h2 id="purchase-order-email-preview-title">{{ preview.subject }}</h2>
          <p>To: {{ preview.recipientEmail }}</p>
        </div>
        <button class="icon-button" type="button" aria-label="Close preview" @click="emit('close')">
          &times;
        </button>
      </header>

      <div class="purchase-order-email-preview">
        <iframe title="Purchase Order email content" sandbox="" :srcdoc="preview.htmlBody"></iframe>
        <footer class="modal-actions">
          <button class="button button-secondary" type="button" @click="emit('close')">
            Close
          </button>
        </footer>
      </div>
    </section>
  </div>
</template>
