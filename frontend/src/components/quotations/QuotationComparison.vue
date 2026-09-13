<script setup lang="ts">
import { computed } from 'vue'

import type { QuotationComparison } from '@/types/quotation'

const props = defineProps<{
  comparison: QuotationComparison
  selectingQuotationId: number | null
}>()

const emit = defineEmits<{
  close: []
  select: [quotationId: number]
}>()

const productRows = computed(() => {
  const products = new Map<number, { code: string; name: string; unit: string; quantity: number }>()
  props.comparison.quotations.forEach((quotation) => {
    quotation.items.forEach((item) => {
      if (!products.has(item.purchaseRequestItemId)) {
        products.set(item.purchaseRequestItemId, {
          code: item.productCode,
          name: item.productName,
          unit: item.unitOfMeasureCode,
          quantity: item.quantity,
        })
      }
    })
  })
  return [...products.entries()].map(([purchaseRequestItemId, product]) => ({
    purchaseRequestItemId,
    ...product,
  }))
})

function unitPrice(quotationId: number, purchaseRequestItemId: number) {
  return props.comparison.quotations
    .find((quotation) => quotation.quotationId === quotationId)
    ?.items.find((item) => item.purchaseRequestItemId === purchaseRequestItemId)?.unitPrice
}

function formatCurrency(value: number | undefined | null) {
  if (value === undefined || value === null) return '-'
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
  <div class="modal-backdrop" @click.self="emit('close')">
    <section
      class="modal-card quotation-comparison-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="quotation-comparison-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Supplier comparison</p>
          <h2 id="quotation-comparison-title">{{ comparison.purchaseRequestNumber }}</h2>
        </div>
        <button
          class="icon-button"
          type="button"
          aria-label="Close comparison"
          @click="emit('close')"
        >
          &times;
        </button>
      </header>

      <div class="quotation-comparison">
        <div v-if="!comparison.quotations.length" class="panel-state">
          <div class="empty-icon" aria-hidden="true">QT</div>
          <strong>No submitted quotations</strong>
          <p>Submit at least one supplier quotation before comparing prices.</p>
        </div>

        <template v-else>
          <section class="comparison-summary">
            <div>
              <span>Submitted quotations</span>
              <strong>{{ comparison.quotations.length }}</strong>
            </div>
            <div>
              <span>Lowest total</span>
              <strong>{{ formatCurrency(comparison.lowestTotalAmount) }}</strong>
            </div>
            <div>
              <span>Selection</span>
              <strong>{{ comparison.selectedQuotationId ? 'Winner selected' : 'Pending' }}</strong>
            </div>
          </section>

          <div class="table-scroll comparison-table">
            <table>
              <thead>
                <tr>
                  <th>Product</th>
                  <th v-for="quotation in comparison.quotations" :key="quotation.quotationId">
                    <span>{{ quotation.supplierName }}</span>
                    <small>{{ quotation.supplierCode }}</small>
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="product in productRows" :key="product.purchaseRequestItemId">
                  <td>
                    <strong>{{ product.name }}</strong>
                    <small>
                      {{ product.code }} &middot; {{ product.quantity }} {{ product.unit }}
                    </small>
                  </td>
                  <td v-for="quotation in comparison.quotations" :key="quotation.quotationId">
                    {{
                      formatCurrency(
                        unitPrice(quotation.quotationId, product.purchaseRequestItemId),
                      )
                    }}
                  </td>
                </tr>
              </tbody>
              <tfoot>
                <tr>
                  <th>Total</th>
                  <td v-for="quotation in comparison.quotations" :key="quotation.quotationId">
                    <strong>{{ formatCurrency(quotation.totalAmount) }}</strong>
                    <span v-if="quotation.isLowestTotal" class="comparison-lowest">Lowest</span>
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>

          <section class="comparison-supplier-cards">
            <article
              v-for="quotation in comparison.quotations"
              :key="quotation.quotationId"
              :class="{ 'is-selected': quotation.isSelected }"
            >
              <div>
                <span>{{ quotation.quotationNumber }}</span>
                <strong>{{ quotation.supplierName }}</strong>
                <small>
                  Valid until {{ formatDate(quotation.validUntil) }}
                  <template v-if="quotation.supplierQuotationReference">
                    &middot; {{ quotation.supplierQuotationReference }}
                  </template>
                </small>
              </div>
              <div class="comparison-card-action">
                <span class="quotation-status" :class="`status-${quotation.status.toLowerCase()}`">
                  {{ quotation.status === 'NotSelected' ? 'Not selected' : quotation.status }}
                </span>
                <button
                  v-if="!comparison.selectedQuotationId && quotation.status === 'Submitted'"
                  class="button button-primary"
                  type="button"
                  :disabled="selectingQuotationId !== null"
                  @click="emit('select', quotation.quotationId)"
                >
                  {{
                    selectingQuotationId === quotation.quotationId
                      ? 'Selecting...'
                      : 'Select winner'
                  }}
                </button>
              </div>
            </article>
          </section>
        </template>

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
