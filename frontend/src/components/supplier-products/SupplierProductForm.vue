<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { Product } from '@/types/product'
import type { Supplier } from '@/types/supplier'
import type { SupplierProduct, SupplierProductFormValues } from '@/types/supplierProduct'

const props = defineProps<{
  supplierProduct: SupplierProduct | null
  suppliers: Supplier[]
  products: Product[]
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: SupplierProductFormValues]
}>()

const form = reactive<SupplierProductFormValues>({
  supplierId: 0,
  productId: 0,
  isPreferred: false,
  isActive: true,
})

const errors = reactive({
  supplierId: '',
  productId: '',
})

const isEditing = computed(() => props.supplierProduct !== null)
const title = computed(() => (isEditing.value ? 'Edit supplier product' : 'New supplier product'))
const activeSuppliers = computed(() => props.suppliers.filter((supplier) => supplier.isActive))
const activeProducts = computed(() => props.products.filter((product) => product.isActive))
const selectedProduct = computed(() =>
  props.products.find((product) => product.id === form.productId),
)
const relatedRecordsAvailable = computed(() => {
  if (!props.supplierProduct) return true

  const supplier = props.suppliers.find((item) => item.id === props.supplierProduct?.supplierId)
  const product = props.products.find((item) => item.id === props.supplierProduct?.productId)
  return Boolean(supplier?.isActive && product?.isActive)
})

watch(
  () => props.supplierProduct,
  (supplierProduct) => {
    form.supplierId = supplierProduct?.supplierId ?? 0
    form.productId = supplierProduct?.productId ?? 0
    form.isPreferred = supplierProduct?.isPreferred ?? false
    form.isActive = supplierProduct?.isActive ?? true
    clearErrors()
  },
  { immediate: true },
)

function clearErrors() {
  errors.supplierId = ''
  errors.productId = ''
}

function validate() {
  clearErrors()

  if (form.supplierId < 1) errors.supplierId = 'Select a supplier.'
  if (form.productId < 1) errors.productId = 'Select a product.'

  return !errors.supplierId && !errors.productId
}

function submit() {
  if (!validate()) return

  emit('save', {
    supplierId: form.supplierId,
    productId: form.productId,
    isPreferred: form.isPreferred,
    isActive: form.isActive,
  })
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat('en-MY', {
    style: 'currency',
    currency: 'MYR',
    minimumFractionDigits: 2,
  }).format(value)
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('cancel')">
    <section
      class="modal-card modal-card-wide"
      role="dialog"
      aria-modal="true"
      aria-labelledby="supplier-product-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Supply relationship</p>
          <h2 id="supplier-product-form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          &times;
        </button>
      </header>

      <form class="supplier-product-form" novalidate @submit.prevent="submit">
        <div v-if="errorMessage" class="form-server-error form-grid-full" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Relationship could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <template v-if="isEditing && supplierProduct">
          <div class="form-field">
            <label for="relationship-supplier-readonly">Supplier</label>
            <input
              id="relationship-supplier-readonly"
              :value="`${supplierProduct.supplierCode} - ${supplierProduct.supplierName}`"
              readonly
            />
            <p class="field-hint">Delete and recreate the relationship to change supplier.</p>
          </div>

          <div class="form-field">
            <label for="relationship-product-readonly">Product</label>
            <input
              id="relationship-product-readonly"
              :value="`${supplierProduct.productCode} - ${supplierProduct.productName}`"
              readonly
            />
            <p class="field-hint">Delete and recreate the relationship to change product.</p>
          </div>
        </template>

        <template v-else>
          <div class="form-field">
            <label for="relationship-supplier">Supplier</label>
            <select
              id="relationship-supplier"
              v-model.number="form.supplierId"
              :aria-invalid="Boolean(errors.supplierId)"
            >
              <option :value="0" disabled>Select a supplier</option>
              <option v-for="supplier in activeSuppliers" :key="supplier.id" :value="supplier.id">
                {{ supplier.code }} - {{ supplier.name }}
              </option>
            </select>
            <p v-if="errors.supplierId" class="field-error">{{ errors.supplierId }}</p>
          </div>

          <div class="form-field">
            <label for="relationship-product">Product</label>
            <select
              id="relationship-product"
              v-model.number="form.productId"
              :aria-invalid="Boolean(errors.productId)"
            >
              <option :value="0" disabled>Select a product</option>
              <option v-for="product in activeProducts" :key="product.id" :value="product.id">
                {{ product.code }} - {{ product.name }}
              </option>
            </select>
            <p v-if="errors.productId" class="field-error">{{ errors.productId }}</p>
          </div>
        </template>

        <div class="relationship-price-preview form-grid-full">
          <span>Product default price</span>
          <strong>
            {{
              selectedProduct
                ? `${formatCurrency(selectedProduct.defaultUnitPrice)} / ${selectedProduct.unitOfMeasureCode}`
                : supplierProduct
                  ? `${formatCurrency(supplierProduct.productDefaultUnitPrice)} / ${supplierProduct.unitOfMeasureCode}`
                  : 'Select a product to view its reference price'
            }}
          </strong>
          <small>Supplier-specific prices will be recorded later in quotations.</small>
        </div>

        <label class="status-control form-grid-full">
          <span>
            <strong>Preferred supplier</strong>
            <small>Highlight this supplier as a preferred option for the selected product.</small>
          </span>
          <input v-model="form.isPreferred" type="checkbox" />
        </label>

        <label v-if="isEditing" class="status-control form-grid-full">
          <span>
            <strong>Active relationship</strong>
            <small>
              {{
                relatedRecordsAvailable
                  ? 'Inactive relationships are excluded from new purchasing activities.'
                  : 'The supplier or product is inactive, so this relationship cannot be reactivated.'
              }}
            </small>
          </span>
          <input v-model="form.isActive" type="checkbox" />
        </label>

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
            {{ saving ? 'Saving...' : isEditing ? 'Save changes' : 'Create relationship' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
