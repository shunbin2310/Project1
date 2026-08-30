<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { Department } from '@/types/department'
import type { Product } from '@/types/product'
import type {
  PurchaseRequest,
  PurchaseRequestFormValues,
  PurchaseRequestItemInput,
} from '@/types/purchaseRequest'

const props = defineProps<{
  purchaseRequest: PurchaseRequest | null
  departments: Department[]
  products: Product[]
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: PurchaseRequestFormValues, submitAfterSave: boolean]
}>()

const form = reactive<PurchaseRequestFormValues>({
  requesterName: null,
  departmentId: null,
  requiredDate: null,
  justification: null,
  items: [],
})

const errors = reactive({
  requesterName: '',
  departmentId: '',
  requiredDate: '',
  justification: '',
  items: '',
})

const isEditing = computed(() => props.purchaseRequest !== null)
const title = computed(() =>
  isEditing.value ? `Edit ${props.purchaseRequest?.requestNumber}` : 'Create purchase request',
)
const selectableDepartments = computed(() =>
  props.departments.filter(
    (department) => department.isActive || department.id === props.purchaseRequest?.departmentId,
  ),
)
const selectableProducts = computed(() => {
  const existingProductIds = new Set(
    props.purchaseRequest?.items.map((item) => item.productId) ?? [],
  )
  return props.products.filter((product) => product.isActive || existingProductIds.has(product.id))
})
const estimatedTotal = computed(() =>
  form.items.reduce((total, item) => total + item.quantity * (item.estimatedUnitPrice ?? 0), 0),
)

watch(
  () => props.purchaseRequest,
  (request) => {
    form.requesterName = request?.requesterName ?? null
    form.departmentId = request?.departmentId ?? null
    form.requiredDate = request?.requiredDate ?? null
    form.justification = request?.justification ?? null
    form.items =
      request?.items.map((item) => ({
        productId: item.productId,
        quantity: item.quantity,
        estimatedUnitPrice: item.estimatedUnitPrice,
      })) ?? []
    clearErrors()
  },
  { immediate: true },
)

function addItem() {
  form.items.push({ productId: 0, quantity: 1, estimatedUnitPrice: null })
}

function removeItem(index: number) {
  form.items.splice(index, 1)
  errors.items = ''
}

function applyDefaultPrice(index: number) {
  const item = form.items[index]
  const product = props.products.find((candidate) => candidate.id === item?.productId)
  if (item && product) item.estimatedUnitPrice = product.defaultUnitPrice
}

function clearErrors() {
  errors.requesterName = ''
  errors.departmentId = ''
  errors.requiredDate = ''
  errors.justification = ''
  errors.items = ''
}

function validate(submitAfterSave: boolean) {
  clearErrors()
  const requesterName = form.requesterName?.trim() ?? ''

  if (submitAfterSave && !requesterName) {
    errors.requesterName = 'Requester name is required before submission.'
  } else if (requesterName && requesterName.length < 2) {
    errors.requesterName = 'Requester name must contain at least 2 characters.'
  } else if (requesterName.length > 100) {
    errors.requesterName = 'Requester name cannot exceed 100 characters.'
  }

  if (submitAfterSave && !form.departmentId) {
    errors.departmentId = 'Department is required before submission.'
  }

  if (submitAfterSave && !form.requiredDate) {
    errors.requiredDate = 'Required date is required before submission.'
  } else if (form.requiredDate && form.requiredDate < new Date().toISOString().slice(0, 10)) {
    errors.requiredDate = 'Required date cannot be in the past.'
  }

  if ((form.justification?.length ?? 0) > 1000) {
    errors.justification = 'Justification cannot exceed 1000 characters.'
  }

  const selectedProductIds = form.items.map((item) => item.productId)
  if (submitAfterSave && form.items.length === 0) {
    errors.items = 'Add at least one item before submission.'
  } else if (selectedProductIds.some((id) => id < 1)) {
    errors.items = 'Select a product for every item.'
  } else if (new Set(selectedProductIds).size !== selectedProductIds.length) {
    errors.items = 'A product can only appear once.'
  } else if (
    submitAfterSave &&
    form.items.some(
      (item) => !props.products.find((product) => product.id === item.productId)?.isActive,
    )
  ) {
    errors.items = 'All products must be active before submission.'
  } else if (
    form.items.some(
      (item) =>
        !Number.isFinite(item.quantity) ||
        (submitAfterSave ? item.quantity <= 0 : item.quantity < 0) ||
        (item.estimatedUnitPrice !== null &&
          (!Number.isFinite(item.estimatedUnitPrice) || item.estimatedUnitPrice < 0)),
    )
  ) {
    errors.items = 'Quantity and estimated price must be zero or greater.'
  }

  return !Object.values(errors).some(Boolean)
}

function submitForm(submitAfterSave: boolean) {
  if (!validate(submitAfterSave)) return

  emit(
    'save',
    {
      requesterName: form.requesterName?.trim() || null,
      departmentId: form.departmentId || null,
      requiredDate: form.requiredDate || null,
      justification: form.justification?.trim() || null,
      items: form.items.map<PurchaseRequestItemInput>((item) => ({
        productId: item.productId,
        quantity: item.quantity,
        estimatedUnitPrice: Number.isFinite(item.estimatedUnitPrice)
          ? item.estimatedUnitPrice
          : null,
      })),
    },
    submitAfterSave,
  )
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat('en-MY', { style: 'currency', currency: 'MYR' }).format(value)
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('cancel')">
    <section
      class="modal-card purchase-request-form-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="purchase-request-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Purchase request draft</p>
          <h2 id="purchase-request-form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          &times;
        </button>
      </header>

      <form class="purchase-request-form" novalidate @submit.prevent="submitForm(false)">
        <div v-if="errorMessage" class="form-server-error form-grid-full" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Purchase request could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div class="form-field">
          <label for="purchase-requester">Requester name</label>
          <input
            id="purchase-requester"
            v-model="form.requesterName"
            maxlength="100"
            placeholder="e.g. Alex Tan"
            :aria-invalid="Boolean(errors.requesterName)"
          />
          <p v-if="errors.requesterName" class="field-error">{{ errors.requesterName }}</p>
          <p v-else class="field-hint">Required before the draft can be submitted.</p>
        </div>

        <div class="form-field">
          <label for="purchase-department">Department</label>
          <select
            id="purchase-department"
            v-model.number="form.departmentId"
            :aria-invalid="Boolean(errors.departmentId)"
          >
            <option :value="null">Not selected</option>
            <option
              v-for="department in selectableDepartments"
              :key="department.id"
              :value="department.id"
              :disabled="!department.isActive"
            >
              {{ department.code }} - {{ department.name }}
            </option>
          </select>
          <p v-if="errors.departmentId" class="field-error">{{ errors.departmentId }}</p>
          <p v-else class="field-hint">Required before submission.</p>
        </div>

        <div class="form-field">
          <label for="purchase-required-date">Required date</label>
          <input
            id="purchase-required-date"
            v-model="form.requiredDate"
            type="date"
            :aria-invalid="Boolean(errors.requiredDate)"
          />
          <p v-if="errors.requiredDate" class="field-error">{{ errors.requiredDate }}</p>
          <p v-else class="field-hint">Required before submission.</p>
        </div>

        <div class="form-field form-grid-full">
          <div class="label-row">
            <label for="purchase-justification">Business justification</label>
            <span>{{ form.justification?.length ?? 0 }}/1000</span>
          </div>
          <textarea
            id="purchase-justification"
            v-model="form.justification"
            maxlength="1000"
            rows="3"
            placeholder="Explain why this purchase is needed"
            :aria-invalid="Boolean(errors.justification)"
          />
          <p v-if="errors.justification" class="field-error">{{ errors.justification }}</p>
        </div>

        <section class="request-items form-grid-full" aria-labelledby="request-items-title">
          <div class="request-items-heading">
            <div>
              <h3 id="request-items-title">Requested items</h3>
              <p>Add products now or save an empty draft and complete it later.</p>
            </div>
            <button class="button button-secondary" type="button" @click="addItem">
              + Add item
            </button>
          </div>

          <p v-if="errors.items" class="field-error request-items-error" role="alert">
            {{ errors.items }}
          </p>

          <div v-if="form.items.length === 0" class="request-items-empty">
            No items added to this draft.
          </div>

          <div v-for="(item, index) in form.items" :key="index" class="request-item-row">
            <div class="form-field request-item-product">
              <label :for="`purchase-product-${index}`">Product</label>
              <select
                :id="`purchase-product-${index}`"
                v-model.number="item.productId"
                @change="applyDefaultPrice(index)"
              >
                <option :value="0" disabled>Select a product</option>
                <option
                  v-for="product in selectableProducts"
                  :key="product.id"
                  :value="product.id"
                  :disabled="!product.isActive"
                >
                  {{ product.code }} - {{ product.name }}
                </option>
              </select>
            </div>

            <div class="form-field">
              <label :for="`purchase-quantity-${index}`">Quantity</label>
              <input
                :id="`purchase-quantity-${index}`"
                v-model.number="item.quantity"
                type="number"
                min="0"
                step="0.001"
              />
            </div>

            <div class="form-field">
              <label :for="`purchase-price-${index}`">Unit price (MYR)</label>
              <input
                :id="`purchase-price-${index}`"
                v-model.number="item.estimatedUnitPrice"
                type="number"
                min="0"
                step="0.01"
              />
            </div>

            <button
              class="icon-button request-item-remove"
              type="button"
              :aria-label="`Remove item ${index + 1}`"
              @click="removeItem(index)"
            >
              &times;
            </button>
          </div>

          <div class="request-total">
            <span>Estimated total</span>
            <strong>{{ formatCurrency(estimatedTotal) }}</strong>
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
            {{ saving ? 'Saving...' : isEditing ? 'Save draft changes' : 'Create draft' }}
          </button>
          <button
            class="button button-primary"
            type="button"
            :disabled="saving"
            @click="submitForm(true)"
          >
            {{ saving ? 'Processing...' : isEditing ? 'Save and submit' : 'Create and submit' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
