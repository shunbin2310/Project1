<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { ProductCategory } from '@/types/productCategory'
import type { Product, ProductFormValues } from '@/types/product'
import type { UnitOfMeasure } from '@/types/unitOfMeasure'

interface ProductFormState extends ProductFormValues {
  code: string
}

const props = defineProps<{
  product: Product | null
  categories: ProductCategory[]
  units: UnitOfMeasure[]
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
  productCategoryId: 0,
  unitOfMeasureId: 0,
  defaultUnitPrice: 0,
  reorderLevel: 0,
  isActive: true,
})

const errors = reactive({
  name: '',
  description: '',
  productCategoryId: '',
  unitOfMeasureId: '',
  defaultUnitPrice: '',
  reorderLevel: '',
})

const isEditing = computed(() => props.product !== null)
const title = computed(() => (isEditing.value ? 'Edit product' : 'Create product'))
const descriptionLength = computed(() => form.description?.length ?? 0)
const selectableCategories = computed(() =>
  props.categories.filter(
    (category) => category.isActive || category.id === props.product?.productCategoryId,
  ),
)
const selectableUnits = computed(() =>
  props.units.filter((unit) => unit.isActive || unit.id === props.product?.unitOfMeasureId),
)

watch(
  () => props.product,
  (product) => {
    form.code = product?.code ?? ''
    form.name = product?.name ?? ''
    form.description = product?.description ?? null
    form.productCategoryId = product?.productCategoryId ?? 0
    form.unitOfMeasureId = product?.unitOfMeasureId ?? 0
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
  errors.productCategoryId = ''
  errors.unitOfMeasureId = ''
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

  if (form.productCategoryId < 1) {
    errors.productCategoryId = 'Select a product category.'
  }

  if (form.unitOfMeasureId < 1) {
    errors.unitOfMeasureId = 'Select a unit of measure.'
  }

  if (!Number.isFinite(form.defaultUnitPrice) || form.defaultUnitPrice < 0) {
    errors.defaultUnitPrice = 'Default unit price must be zero or greater.'
  }

  if (!Number.isFinite(form.reorderLevel) || form.reorderLevel < 0) {
    errors.reorderLevel = 'Reorder level must be zero or greater.'
  }

  return !Object.values(errors).some(Boolean)
}

function submit() {
  if (!validate()) return

  emit('save', {
    name: form.name.trim(),
    description: form.description?.trim() || null,
    productCategoryId: form.productCategoryId,
    unitOfMeasureId: form.unitOfMeasureId,
    defaultUnitPrice: form.defaultUnitPrice,
    reorderLevel: form.reorderLevel,
    isActive: form.isActive,
  })
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('cancel')">
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
          <label for="product-category">Product category</label>
          <select
            id="product-category"
            v-model.number="form.productCategoryId"
            :aria-invalid="Boolean(errors.productCategoryId)"
          >
            <option :value="0" disabled>Select a category</option>
            <option
              v-for="category in selectableCategories"
              :key="category.id"
              :value="category.id"
              :disabled="!category.isActive"
            >
              {{ category.code }} - {{ category.name }}{{ category.isActive ? '' : ' (Inactive)' }}
            </option>
          </select>
          <p v-if="errors.productCategoryId" class="field-error">
            {{ errors.productCategoryId }}
          </p>
        </div>

        <div class="form-field">
          <label for="product-unit">Unit of measure</label>
          <select
            id="product-unit"
            v-model.number="form.unitOfMeasureId"
            :aria-invalid="Boolean(errors.unitOfMeasureId)"
          >
            <option :value="0" disabled>Select a unit</option>
            <option
              v-for="unit in selectableUnits"
              :key="unit.id"
              :value="unit.id"
              :disabled="!unit.isActive"
            >
              {{ unit.code }} - {{ unit.name }}{{ unit.isActive ? '' : ' (Inactive)' }}
            </option>
          </select>
          <p v-if="errors.unitOfMeasureId" class="field-error">
            {{ errors.unitOfMeasureId }}
          </p>
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
            max="999999999999999.999"
            step="0.001"
            inputmode="decimal"
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
