<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { Product, ProductFormValues } from '@/types/product'

interface ProductFormState extends ProductFormValues {
  code: string
}

const props = defineProps<{
  product: Product | null
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: ProductFormValues]
}>()

const form = reactive<ProductFormState>({
  code: '',
  name: '',
  description: null,
  storageLocation: null,
  defaultUnitPrice: 0,
  reorderLevel: 0,
  isActive: true,
})

const errors = reactive({
  name: '',
  description: '',
  storageLocation: '',
  defaultUnitPrice: '',
  reorderLevel: '',
})

const isEditing = computed(() => props.product !== null)
const title = computed(() => (isEditing.value ? 'Edit product' : 'Create product'))
const descriptionLength = computed(() => form.description?.length ?? 0)
watch(
  () => props.product,
  (product) => {
    form.code = product?.code ?? ''
    form.name = product?.name ?? ''
    form.description = product?.description ?? null
    form.storageLocation = product?.storageLocation ?? null
    form.defaultUnitPrice = product?.defaultUnitPrice ?? 0
    form.reorderLevel = product?.reorderLevel ?? 0
    form.isActive = product?.isActive ?? true
    clearErrors()
  },
  { immediate: true },
)

function clearErrors() {
  errors.name = ''
  errors.description = ''
  errors.storageLocation = ''
  errors.defaultUnitPrice = ''
  errors.reorderLevel = ''
}

function validate() {
  clearErrors()
  const name = form.name.trim()

  if (name.length < 2) {
    errors.name = 'Name must contain at least 2 characters.'
  } else if (name.length > 150) {
    errors.name = 'Name cannot exceed 150 characters.'
  }

  if ((form.description?.length ?? 0) > 500) {
    errors.description = 'Description cannot exceed 500 characters.'
  }

  if ((form.storageLocation?.length ?? 0) > 100) {
    errors.storageLocation = 'Storage location cannot exceed 100 characters.'
  }

  if (!Number.isFinite(form.defaultUnitPrice) || form.defaultUnitPrice < 0) {
    errors.defaultUnitPrice = 'Default unit price must be zero or greater.'
  }

  if (
    !Number.isSafeInteger(form.reorderLevel) ||
    form.reorderLevel < 0 ||
    form.reorderLevel > 999999999999999
  ) {
    errors.reorderLevel = 'Reorder level must be a non-negative whole number.'
  }

  return !Object.values(errors).some(Boolean)
}

function submit() {
  if (!validate()) return

  emit('save', {
    name: form.name.trim(),
    description: form.description?.trim() || null,
    storageLocation: form.storageLocation?.trim() || null,
    defaultUnitPrice: form.defaultUnitPrice,
    reorderLevel: form.reorderLevel,
    isActive: form.isActive,
  })
}
</script>

<template>
  <div class="modal-backdrop">
    <section
      class="modal-card modal-card-wide"
      role="dialog"
      aria-modal="true"
      aria-labelledby="product-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Product details</p>
          <h2 id="product-form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          &times;
        </button>
      </header>

      <form class="product-form" novalidate @submit.prevent="submit">
        <div v-if="errorMessage" class="form-server-error form-grid-full" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Product could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div v-if="isEditing" class="form-field">
          <label for="product-code">Product code</label>
          <input id="product-code" v-model="form.code" autocomplete="off" readonly />
          <p class="field-hint">Product code is generated automatically and cannot be changed.</p>
        </div>

        <div class="form-field" :class="{ 'form-grid-full': !isEditing }">
          <label for="product-name">Product name</label>
          <input
            id="product-name"
            v-model="form.name"
            maxlength="150"
            autocomplete="off"
            placeholder="e.g. A4 Copy Paper"
            :aria-invalid="Boolean(errors.name)"
            :aria-describedby="errors.name ? 'product-name-error' : undefined"
          />
          <p v-if="errors.name" id="product-name-error" class="field-error">{{ errors.name }}</p>
        </div>

        <div class="form-field">
          <label for="product-price">Default unit price (MYR)</label>
          <input
            id="product-price"
            v-model.number="form.defaultUnitPrice"
            type="number"
            min="0"
            max="9999999999999999.99"
            step="0.01"
            inputmode="decimal"
            :aria-invalid="Boolean(errors.defaultUnitPrice)"
          />
          <p v-if="errors.defaultUnitPrice" class="field-error">
            {{ errors.defaultUnitPrice }}
          </p>
        </div>

        <div class="form-field">
          <label for="product-reorder-level">Reorder level</label>
          <input
            id="product-reorder-level"
            v-model.number="form.reorderLevel"
            type="number"
            min="0"
            max="999999999999999"
            step="1"
            inputmode="numeric"
            :aria-invalid="Boolean(errors.reorderLevel)"
          />
          <p v-if="errors.reorderLevel" class="field-error">{{ errors.reorderLevel }}</p>
        </div>

        <div class="form-field form-grid-full">
          <div class="label-row">
            <label for="product-description">Description</label>
            <span>{{ descriptionLength }}/500</span>
          </div>
          <textarea
            id="product-description"
            v-model="form.description"
            maxlength="500"
            rows="3"
            placeholder="Add useful purchasing or inventory details"
            :aria-invalid="Boolean(errors.description)"
          />
          <p v-if="errors.description" class="field-error">{{ errors.description }}</p>
        </div>

        <div class="form-field form-grid-full">
          <label for="product-storage-location">Storage location</label>
          <input
            id="product-storage-location"
            v-model="form.storageLocation"
            maxlength="100"
            autocomplete="off"
            placeholder="e.g. Shelf A1 (optional)"
            :aria-invalid="Boolean(errors.storageLocation)"
            :aria-describedby="
              errors.storageLocation ? 'product-storage-location-error' : undefined
            "
          />
          <p v-if="errors.storageLocation" id="product-storage-location-error" class="field-error">
            {{ errors.storageLocation }}
          </p>
        </div>

        <label v-if="isEditing" class="status-control form-grid-full">
          <span>
            <strong>Active product</strong>
            <small>Inactive products are unavailable for new purchasing activities.</small>
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
            {{ saving ? 'Saving...' : isEditing ? 'Save changes' : 'Create product' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
