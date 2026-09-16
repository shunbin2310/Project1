<script setup lang="ts">
import type { GoodsReceipt } from '@/types/goodsReceipt'

defineProps<{
  goodsReceipt: GoodsReceipt
}>()

const emit = defineEmits<{
  close: []
}>()

function formatDate(value: string) {
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
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('close')">
    <section
      class="modal-card goods-receipt-details-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="goods-receipt-details-title"
    >
      <header class="modal-header">
        <div class="goods-receipt-details-title">
          <p class="eyebrow">Goods receipt</p>
          <div>
            <h2 id="goods-receipt-details-title">{{ goodsReceipt.goodsReceiptNumber }}</h2>
            <span
              class="goods-receipt-status"
              :class="`status-${goodsReceipt.status.toLowerCase()}`"
            >
              {{ goodsReceipt.status }}
            </span>
          </div>
        </div>
        <button class="icon-button" type="button" aria-label="Close details" @click="emit('close')">
          &times;
        </button>
      </header>

      <div class="goods-receipt-details">
        <section class="goods-receipt-summary">
          <div class="goods-receipt-summary-primary">
            <div>
              <span>Supplier</span>
              <strong>{{ goodsReceipt.supplierName }}</strong>
              <small>{{ goodsReceipt.supplierCode }}</small>
            </div>
            <div>
              <span>Purchase order</span>
              <strong>{{ goodsReceipt.purchaseOrderNumber }}</strong>
              <small>Receiving source</small>
            </div>
            <div>
              <span>Delivery note</span>
              <strong>{{ goodsReceipt.supplierDeliveryNoteNumber || 'Not provided' }}</strong>
              <small>Supplier reference</small>
            </div>
          </div>
          <div class="goods-receipt-summary-meta">
            <div>
              <span>Received date</span>
              <strong>{{ formatDate(goodsReceipt.receivedDate) }}</strong>
            </div>
            <div>
              <span>Line items</span>
              <strong>{{ goodsReceipt.items.length }}</strong>
            </div>
            <div>
              <span>Total received</span>
              <strong>
                {{
                  formatQuantity(
                    goodsReceipt.items.reduce((sum, item) => sum + item.quantityReceived, 0),
                  )
                }}
              </strong>
            </div>
          </div>
        </section>

        <section class="details-section">
          <div class="details-section-heading">
            <div>
              <h3>Received items</h3>
              <p>Quantities recorded for this delivery</p>
            </div>
          </div>
          <div class="details-table table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Product</th>
                  <th>PO quantity</th>
                  <th>Received in this receipt</th>
                  <th>Unit</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in goodsReceipt.items" :key="item.id">
                  <td>
                    <strong>{{ item.productName }}</strong>
                    <small>{{ item.productCode }}</small>
                  </td>
                  <td>{{ formatQuantity(item.orderedQuantity) }}</td>
                  <td>
                    <strong>{{ formatQuantity(item.quantityReceived) }}</strong>
                  </td>
                  <td>{{ item.unitOfMeasureCode }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <section class="details-section">
          <div class="details-section-heading"><h3>Receiving notes</h3></div>
          <p class="justification-copy">{{ goodsReceipt.notes || 'No notes provided.' }}</p>
        </section>

        <section class="goods-receipt-audit" aria-label="Goods receipt audit information">
          <div>
            <span>Created</span>
            <strong>{{ goodsReceipt.createdByName }}</strong>
            <small>{{ formatDateTime(goodsReceipt.createdAtUtc) }}</small>
          </div>
          <div v-if="goodsReceipt.postedAtUtc">
            <span>Posted</span>
            <strong>{{ goodsReceipt.postedByName }}</strong>
            <small>{{ formatDateTime(goodsReceipt.postedAtUtc) }}</small>
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
