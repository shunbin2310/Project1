<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import type { Supplier, SupplierFormValues } from '@/types/supplier'

interface SupplierFormState extends SupplierFormValues {
  code: string
}

const props = defineProps<{
  supplier: Supplier | null
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: SupplierFormValues]
}>()

const form = reactive<SupplierFormState>({
  code: '',
  name: '',
  contactPerson: null,
  email: null,
  phone: null,
  address: null,
  isActive: true,
})

const errors = reactive({
  name: '',
  contactPerson: '',
  email: '',
  phone: '',
  address: '',
})

const isEditing = computed(() => props.supplier !== null)
const title = computed(() => (isEditing.value ? 'Edit supplier' : 'Create supplier'))
const addressLength = computed(() => form.address?.length ?? 0)

watch(
  () => props.supplier,
  (supplier) => {
    form.code = supplier?.code ?? ''
    form.name = supplier?.name ?? ''
    form.contactPerson = supplier?.contactPerson ?? null
    form.email = supplier?.email ?? null
    form.phone = supplier?.phone ?? null
    form.address = supplier?.address ?? null
    form.isActive = supplier?.isActive ?? true
    clearErrors()
  },
  { immediate: true },
)

function clearErrors() {
  errors.name = ''
  errors.contactPerson = ''
  errors.email = ''
  errors.phone = ''
  errors.address = ''
}

function validate() {
  clearErrors()
  const name = form.name.trim()
  const email = form.email?.trim() ?? ''

  if (name.length < 2) {
    errors.name = 'Name must contain at least 2 characters.'
  } else if (name.length > 150) {
    errors.name = 'Name cannot exceed 150 characters.'
  }

  if ((form.contactPerson?.length ?? 0) > 100) {
    errors.contactPerson = 'Contact person cannot exceed 100 characters.'
  }

  if (email.length > 254) {
    errors.email = 'Email cannot exceed 254 characters.'
  } else if (email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    errors.email = 'Enter a valid email address.'
  }

  if ((form.phone?.length ?? 0) > 30) {
    errors.phone = 'Phone cannot exceed 30 characters.'
  }

  if ((form.address?.length ?? 0) > 500) {
    errors.address = 'Address cannot exceed 500 characters.'
  }

  return !Object.values(errors).some(Boolean)
}

function normalizeOptional(value: string | null) {
  return value?.trim() || null
}

function submit() {
  if (!validate()) return

  emit('save', {
    name: form.name.trim(),
    contactPerson: normalizeOptional(form.contactPerson),
    email: normalizeOptional(form.email),
    phone: normalizeOptional(form.phone),
    address: normalizeOptional(form.address),
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
      aria-labelledby="supplier-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Supplier details</p>
          <h2 id="supplier-form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          ×
        </button>
      </header>

      <form class="supplier-form" novalidate @submit.prevent="submit">
        <div v-if="errorMessage" class="form-server-error form-grid-full" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Supplier could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div v-if="isEditing" class="form-field">
          <label for="supplier-code">Supplier code</label>
          <input
            id="supplier-code"
            v-model="form.code"
            maxlength="20"
            autocomplete="off"
            readonly
          />
          <p class="field-hint">Supplier code cannot be changed after creation.</p>
        </div>

        <div class="form-field" :class="{ 'form-grid-full': !isEditing }">
          <label for="supplier-name">Supplier name</label>
          <input
            id="supplier-name"
            v-model="form.name"
            maxlength="150"
            autocomplete="organization"
            placeholder="e.g. Example Supplies Sdn. Bhd."
            :aria-invalid="Boolean(errors.name)"
            :aria-describedby="errors.name ? 'supplier-name-error' : undefined"
          />
          <p v-if="errors.name" id="supplier-name-error" class="field-error">
            {{ errors.name }}
          </p>
        </div>

        <div class="form-field">
          <label for="supplier-contact">Contact person</label>
          <input
            id="supplier-contact"
            v-model="form.contactPerson"
            maxlength="100"
            autocomplete="name"
            placeholder="e.g. Alex Tan"
            :aria-invalid="Boolean(errors.contactPerson)"
          />
          <p v-if="errors.contactPerson" class="field-error">{{ errors.contactPerson }}</p>
        </div>

        <div class="form-field">
          <label for="supplier-email">Email</label>
          <input
            id="supplier-email"
            v-model="form.email"
            type="email"
            maxlength="254"
            autocomplete="email"
            placeholder="e.g. sales@example.com"
            :aria-invalid="Boolean(errors.email)"
            :aria-describedby="errors.email ? 'supplier-email-error' : undefined"
          />
          <p v-if="errors.email" id="supplier-email-error" class="field-error">
            {{ errors.email }}
          </p>
        </div>

        <div class="form-field form-grid-full">
          <label for="supplier-phone">Phone</label>
          <input
            id="supplier-phone"
            v-model="form.phone"
            type="tel"
            maxlength="30"
            autocomplete="tel"
            placeholder="e.g. +60 3-1234 5678"
            :aria-invalid="Boolean(errors.phone)"
          />
          <p v-if="errors.phone" class="field-error">{{ errors.phone }}</p>
        </div>

        <div class="form-field form-grid-full">
          <div class="label-row">
            <label for="supplier-address">Address</label>
            <span>{{ addressLength }}/500</span>
          </div>
          <textarea
            id="supplier-address"
            v-model="form.address"
            maxlength="500"
            rows="3"
            autocomplete="street-address"
            placeholder="Enter the supplier's business address"
            :aria-invalid="Boolean(errors.address)"
          />
          <p v-if="errors.address" class="field-error">{{ errors.address }}</p>
        </div>

        <label v-if="isEditing" class="status-control form-grid-full">
          <span>
            <strong>Active supplier</strong>
            <small>Inactive suppliers are unavailable for new purchase requests.</small>
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
            {{ saving ? 'Saving…' : isEditing ? 'Save changes' : 'Create supplier' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
