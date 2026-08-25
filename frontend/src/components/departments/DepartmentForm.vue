<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { Department, DepartmentFormValues } from '@/types/department'

const props = defineProps<{
  department: Department | null
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: DepartmentFormValues]
}>()

const form = reactive<DepartmentFormValues>({
  code: '',
  name: '',
  description: null,
  isActive: true,
})

const errors = reactive({
  code: '',
  name: '',
  description: '',
})

const isEditing = computed(() => props.department !== null)
const title = computed(() => (isEditing.value ? 'Edit department' : 'Create department'))
const descriptionLength = computed(() => form.description?.length ?? 0)

watch(
  () => props.department,
  (department) => {
    form.code = department?.code ?? ''
    form.name = department?.name ?? ''
    form.description = department?.description ?? null
    form.isActive = department?.isActive ?? true
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

  if (code.length < 2) {
    errors.code = 'Code must contain at least 2 characters.'
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

  return !errors.code && !errors.name && !errors.description
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
    <section class="modal-card" role="dialog" aria-modal="true" :aria-labelledby="'form-title'">
      <header class="modal-header">
        <div>
          <p class="eyebrow">Department details</p>
          <h2 id="form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          ×
        </button>
      </header>

      <form class="department-form" novalidate @submit.prevent="submit">
        <div v-if="errorMessage" class="form-server-error" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Department could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div class="form-field">
          <label for="department-code">Department code</label>
          <input
            id="department-code"
            v-model="form.code"
            maxlength="20"
            autocomplete="off"
            placeholder="e.g. FIN"
            :readonly="isEditing"
            :aria-invalid="Boolean(errors.code)"
            :aria-describedby="errors.code ? 'department-code-error' : undefined"
          />
          <p v-if="errors.code" id="department-code-error" class="field-error">
            {{ errors.code }}
          </p>
          <p v-else class="field-hint">
            {{
              isEditing
                ? 'Department code cannot be changed after creation.'
                : 'A unique 2–20 character identifier.'
            }}
          </p>
        </div>

        <div class="form-field">
          <label for="department-name">Department name</label>
          <input
            id="department-name"
            v-model="form.name"
            maxlength="100"
            autocomplete="organization"
            placeholder="e.g. Finance"
            :aria-invalid="Boolean(errors.name)"
            :aria-describedby="errors.name ? 'department-name-error' : undefined"
          />
          <p v-if="errors.name" id="department-name-error" class="field-error">
            {{ errors.name }}
          </p>
        </div>

        <div class="form-field">
          <div class="label-row">
            <label for="department-description">Description</label>
            <span>{{ descriptionLength }}/500</span>
          </div>
          <textarea
            id="department-description"
            v-model="form.description"
            maxlength="500"
            rows="4"
            placeholder="Describe the department's responsibilities"
            :aria-invalid="Boolean(errors.description)"
          />
          <p v-if="errors.description" class="field-error">{{ errors.description }}</p>
        </div>

        <label v-if="isEditing" class="status-control">
          <span>
            <strong>Active department</strong>
            <small>Inactive departments are hidden from normal lists.</small>
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
            {{ saving ? 'Saving…' : isEditing ? 'Save changes' : 'Create department' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
