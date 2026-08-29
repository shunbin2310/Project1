<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { UnitOfMeasure, UnitOfMeasureFormValues } from '@/types/unitOfMeasure'

const props = defineProps<{
  unit: UnitOfMeasure | null
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: UnitOfMeasureFormValues]
}>()

const form = reactive<UnitOfMeasureFormValues>({
  code: '',
  name: '',
  description: null,
  isActive: true,
})

const errors = reactive({ code: '', name: '', description: '' })
const isEditing = computed(() => props.unit !== null)
const title = computed(() => (isEditing.value ? 'Edit unit of measure' : 'Create unit of measure'))
const descriptionLength = computed(() => form.description?.length ?? 0)

watch(
  () => props.unit,
  (unit) => {
    form.code = unit?.code ?? ''
    form.name = unit?.name ?? ''
    form.description = unit?.description ?? null
    form.isActive = unit?.isActive ?? true
    clearErrors()
  },
  { immediate: true },
)

function clearErrors() {
  errors.code = ''
  errors.name = ''
  errors.description = ''
}

function validate() {
  clearErrors()
  const code = form.code.trim()
  const name = form.name.trim()

  if (!code) {
    errors.code = 'Code is required.'
  } else if (code.length > 20) {
    errors.code = 'Code cannot exceed 20 characters.'
  }

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
    code: form.code.trim().toUpperCase(),
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
      aria-labelledby="unit-of-measure-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Unit details</p>
          <h2 id="unit-of-measure-form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          &times;
        </button>
      </header>

      <form class="unit-form" novalidate @submit.prevent="submit">
        <div v-if="errorMessage" class="form-server-error" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Unit of measure could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div class="form-field">
          <label for="unit-code">Unit code</label>
          <input
            id="unit-code"
            v-model="form.code"
            maxlength="20"
            autocomplete="off"
            placeholder="e.g. PCS"
            :readonly="isEditing"
            :aria-invalid="Boolean(errors.code)"
            :aria-describedby="errors.code ? 'unit-code-error' : undefined"
          />
          <p v-if="errors.code" id="unit-code-error" class="field-error">{{ errors.code }}</p>
          <p v-else-if="isEditing" class="field-hint">
            Unit code cannot be changed after creation.
          </p>
          <p v-else class="field-hint">Use a short standard code such as PCS, KG, or BOX.</p>
        </div>

        <div class="form-field">
          <label for="unit-name">Unit name</label>
          <input
            id="unit-name"
            v-model="form.name"
            maxlength="100"
            autocomplete="off"
            placeholder="e.g. Pieces"
            :aria-invalid="Boolean(errors.name)"
            :aria-describedby="errors.name ? 'unit-name-error' : undefined"
          />
          <p v-if="errors.name" id="unit-name-error" class="field-error">{{ errors.name }}</p>
        </div>

        <div class="form-field">
          <div class="label-row">
            <label for="unit-description">Description</label>
            <span>{{ descriptionLength }}/500</span>
          </div>
          <textarea
            id="unit-description"
            v-model="form.description"
            maxlength="500"
            rows="4"
            placeholder="Explain how this unit is used"
            :aria-invalid="Boolean(errors.description)"
          />
          <p v-if="errors.description" class="field-error">{{ errors.description }}</p>
        </div>

        <label v-if="isEditing" class="status-control">
          <span>
            <strong>Active unit</strong>
            <small>Inactive units are unavailable when maintaining products.</small>
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
            {{ saving ? 'Saving...' : isEditing ? 'Save changes' : 'Create unit' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
