<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { ProductCategory, ProductCategoryFormValues } from '@/types/productCategory'

interface ProductCategoryFormState extends ProductCategoryFormValues {
  code: string
}

const props = defineProps<{
  category: ProductCategory | null
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: ProductCategoryFormValues]
}>()

const form = reactive<ProductCategoryFormState>({
  code: '',
  name: '',
  description: null,
  isActive: true,
})

const errors = reactive({ name: '', description: '' })
const isEditing = computed(() => props.category !== null)
const title = computed(() =>
  isEditing.value ? 'Edit product category' : 'Create product category',
)
const descriptionLength = computed(() => form.description?.length ?? 0)

watch(
  () => props.category,
  (category) => {
    form.code = category?.code ?? ''
    form.name = category?.name ?? ''
    form.description = category?.description ?? null
    form.isActive = category?.isActive ?? true
    clearErrors()
  },
  { immediate: true },
)

function clearErrors() {
  errors.name = ''
  errors.description = ''
}

function validate() {
  clearErrors()
  const name = form.name.trim()

  if (name.length < 2) {
    errors.name = 'Name must contain at least 2 characters.'
  } else if (name.length > 100) {
    errors.name = 'Name cannot exceed 100 characters.'
  }

  if ((form.description?.length ?? 0) > 500) {
    errors.description = 'Description cannot exceed 500 characters.'
  }

  return !Object.values(errors).some(Boolean)
}

function submit() {
  if (!validate()) return

  emit('save', {
    name: form.name.trim(),
    description: form.description?.trim() || null,
    isActive: form.isActive,
  })
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('cancel')">
    <section
      class="modal-card"
      role="dialog"
      aria-modal="true"
      aria-labelledby="product-category-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Category details</p>
          <h2 id="product-category-form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          &times;
        </button>
      </header>

      <form class="category-form" novalidate @submit.prevent="submit">
        <div v-if="errorMessage" class="form-server-error" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Product category could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div v-if="isEditing" class="form-field">
          <label for="product-category-code">Category code</label>
          <input id="product-category-code" v-model="form.code" autocomplete="off" readonly />
          <p class="field-hint">Category code is generated automatically and cannot be changed.</p>
        </div>

        <div class="form-field">
          <label for="product-category-name">Category name</label>
          <input
            id="product-category-name"
            v-model="form.name"
            maxlength="100"
            autocomplete="off"
            placeholder="e.g. Office Supplies"
            :aria-invalid="Boolean(errors.name)"
            :aria-describedby="errors.name ? 'product-category-name-error' : undefined"
          />
          <p v-if="errors.name" id="product-category-name-error" class="field-error">
            {{ errors.name }}
          </p>
        </div>

        <div class="form-field">
          <div class="label-row">
            <label for="product-category-description">Description</label>
            <span>{{ descriptionLength }}/500</span>
          </div>
          <textarea
            id="product-category-description"
            v-model="form.description"
            maxlength="500"
            rows="4"
            placeholder="Explain which products belong in this category"
            :aria-invalid="Boolean(errors.description)"
          />
          <p v-if="errors.description" class="field-error">{{ errors.description }}</p>
        </div>

        <label v-if="isEditing" class="status-control">
          <span>
            <strong>Active category</strong>
            <small>Inactive categories are unavailable when maintaining products.</small>
          </span>
          <input v-model="form.isActive" type="checkbox" />
        </label>

        <footer class="modal-actions">
          <button
            class="button button-secondary"
            type="button"
            :disabled="saving"
            @click="emit('cancel')"
          >
            Cancel
          </button>
          <button class="button button-primary" type="submit" :disabled="saving">
            {{ saving ? 'Saving...' : isEditing ? 'Save changes' : 'Create product category' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
