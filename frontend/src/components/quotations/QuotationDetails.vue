<script setup lang="ts">
import type { Quotation } from '@/types/quotation'

defineProps<{
  quotation: Quotation
}>()

const emit = defineEmits<{
  close: []
}>()

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
      class="modal-card quotation-details-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="quotation-details-title"
    >
      <header class="modal-header quotation-details-header">
        <div class="quotation-details-title">
          <p class="eyebrow">Supplier quotation</p>
          <div>
            <h2 id="quotation-details-title">{{ quotation.quotationNumber }}</h2>
            <span class="quotation-status" :class="`status-${quotation.status.toLowerCase()}`">
              {{ quotation.status }}
            </span>
          </div>
        </div>
        <button class="icon-button" type="button" aria-label="Close details" @click="emit('close')">
          &times;
        </button>
      </header>

      <div class="quotation-details">
        <section class="quotation-summary-card" aria-label="Quotation summary">
          <div class="quotation-summary-primary">
            <div>
              <span class="quotation-summary-label">Supplier</span>
              <strong class="quotation-summary-supplier">{{ quotation.supplierName }}</strong>
              <small>{{ quotation.supplierCode }}</small>
            </div>
            <div class="quotation-summary-request">
              <span class="quotation-summary-label">Purchase request</span>
              <strong>{{ quotation.purchaseRequestNumber }}</strong>
              <small>Approved request</small>
            </div>
          </div>

          <div class="quotation-summary-meta">
            <div>
              <span class="quotation-summary-label">Supplier reference</span>
              <strong>{{ quotation.supplierQuotationReference || 'Not provided' }}</strong>
            </div>
            <div>
              <span class="quotation-summary-label">Quotation date</span>
              <strong>{{ formatDate(quotation.quotationDate) }}</strong>
            </div>
            <div>
              <span class="quotation-summary-label">Valid until</span>
              <strong>{{ formatDate(quotation.validUntil) }}</strong>
            </div>
          </div>
        </section>

        <section class="details-section">
          <div class="details-section-heading">
            <div>
              <h3>Quoted items</h3>
              <p>Prices supplied for the approved request quantities</p>
            </div>
          </div>
          <div class="table-scroll details-table">
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
                <tr v-for="item in quotation.items" :key="item.id">
                  <td>
                    <strong>{{ item.productName }}</strong>
                    <small>{{ item.productCode }}</small>
                  </td>
                  <td>{{ formatQuantity(item.quantity) }} {{ item.unitOfMeasureCode }}</td>
                  <td>{{ formatCurrency(item.unitPrice) }}</td>
                  <td>{{ formatCurrency(item.lineTotal) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="quotation-details-total">
            <span>Total amount</span>
            <strong>{{ formatCurrency(quotation.totalAmount) }}</strong>
          </div>
        </section>

        <section class="details-section">
          <div class="details-section-heading">
            <div><h3>Supplier notes</h3></div>
          </div>
          <p class="justification-copy">{{ quotation.notes || 'No notes provided.' }}</p>
        </section>

        <section class="quotation-audit">
          <span>Created by {{ quotation.createdByName }}</span>
          <span>{{ formatDateTime(quotation.createdAtUtc) }}</span>
        </section>

        <footer class="details-footer">
          <span></span>
          <button class="button button-secondary" type="button" @click="emit('close')">
            Close
          </button>
        </footer>
      </div>
    </section>
  </div>
</template>
