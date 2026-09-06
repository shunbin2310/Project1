<script setup lang="ts">
import { computed, reactive, watch } from 'vue'

import { applicationRoles, type ApplicationRole } from '@/types/auth'
import type { Department } from '@/types/department'
import type { User, UserFormValues } from '@/types/user'

const props = defineProps<{
  user: User | null
  currentUserId: number | null
  availableRoles: ApplicationRole[]
  departments: Department[]
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: UserFormValues]
}>()

const form = reactive<UserFormValues>({
  email: '',
  fullName: '',
  departmentId: null,
  roles: [],
})
const errors = reactive({ email: '', fullName: '', roles: '' })

const isEditing = computed(() => props.user !== null)
const isEditingCurrentUser = computed(() => props.user?.id === props.currentUserId)
const title = computed(() => (isEditing.value ? 'Edit user' : 'Create user'))

watch(
  () => props.user,
  (user) => {
    form.email = user?.email ?? ''
    form.fullName = user?.fullName ?? ''
    form.departmentId = user?.departmentId ?? null
    form.roles = user?.roles.includes(applicationRoles.admin)
      ? [applicationRoles.admin]
      : [...(user?.roles ?? [])]
    clearErrors()
  },
  { immediate: true },
)

function roleLabel(role: string) {
  return role
    .toLowerCase()
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}

function locksRole(role: ApplicationRole) {
  return (
    (isEditingCurrentUser.value && role === applicationRoles.admin) ||
    (form.roles.includes(applicationRoles.admin) && role !== applicationRoles.admin)
  )
}

function roleHint(role: ApplicationRole) {
  if (isEditingCurrentUser.value && role === applicationRoles.admin) {
    return 'You cannot remove your own Admin role.'
  }

  if (form.roles.includes(applicationRoles.admin) && role !== applicationRoles.admin) {
    return 'Admin already has full access.'
  }

  return role
}

function toggleRole(role: ApplicationRole, event: Event) {
  const checked = (event.target as HTMLInputElement).checked

  if (role === applicationRoles.admin && checked) {
    form.roles = [applicationRoles.admin]
    return
  }

  if (role !== applicationRoles.admin && checked) {
    form.roles = form.roles.filter((selectedRole) => selectedRole !== applicationRoles.admin)
  }

  form.roles = checked
    ? [...new Set([...form.roles, role])]
    : form.roles.filter((selectedRole) => selectedRole !== role)
}

function clearErrors() {
  errors.email = ''
  errors.fullName = ''
  errors.roles = ''
}

function validate() {
  clearErrors()
  const email = form.email.trim()
  const fullName = form.fullName.trim()

  if (!isEditing.value && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    errors.email = 'Enter a valid email address.'
  }
  if (fullName.length < 2) errors.fullName = 'Full name must contain at least 2 characters.'
  else if (fullName.length > 100) errors.fullName = 'Full name cannot exceed 100 characters.'
  if (form.roles.length === 0) errors.roles = 'Select at least one role.'

  return !errors.email && !errors.fullName && !errors.roles
}

function submit() {
  if (!validate()) return

  emit('save', {
    email: form.email.trim().toLowerCase(),
    fullName: form.fullName.trim(),
    departmentId: form.departmentId || null,
    roles: [...form.roles],
  })
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('cancel')">
    <section
      class="modal-card modal-card-wide"
      role="dialog"
      aria-modal="true"
      aria-labelledby="user-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Account details</p>
          <h2 id="user-form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          ×
        </button>
      </header>

      <form class="user-form" novalidate @submit.prevent="submit">
        <div v-if="errorMessage" class="form-server-error form-grid-full" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>User could not be saved</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div class="form-field">
          <label for="user-email">Email</label>
          <input
            id="user-email"
            v-model="form.email"
            type="email"
            maxlength="256"
            autocomplete="email"
            placeholder="name@company.com"
            :readonly="isEditing"
            :aria-invalid="Boolean(errors.email)"
          />
          <p v-if="errors.email" class="field-error">{{ errors.email }}</p>
          <p v-else-if="isEditing" class="field-hint">Email cannot be changed after creation.</p>
        </div>

        <div class="form-field">
          <label for="user-full-name">Full name</label>
          <input
            id="user-full-name"
            v-model="form.fullName"
            maxlength="100"
            autocomplete="name"
            placeholder="e.g. Alex Tan"
            :aria-invalid="Boolean(errors.fullName)"
          />
          <p v-if="errors.fullName" class="field-error">{{ errors.fullName }}</p>
        </div>

        <div class="form-field">
          <label for="user-department">Department</label>
          <select id="user-department" v-model="form.departmentId">
            <option :value="null">No department</option>
            <option v-for="department in departments" :key="department.id" :value="department.id">
              {{ department.code }} — {{ department.name }}
            </option>
          </select>
        </div>

        <fieldset
          class="role-selection form-grid-full"
          :aria-describedby="errors.roles ? 'user-roles-error' : undefined"
        >
          <legend>Roles</legend>
          <div class="role-option-grid">
            <label
              v-for="role in availableRoles"
              :key="role"
              class="role-option"
              :class="{ 'is-locked': locksRole(role) }"
            >
              <input
                type="checkbox"
                :value="role"
                :checked="form.roles.includes(role)"
                :disabled="locksRole(role)"
                @change="toggleRole(role, $event)"
              />
              <span>
                <strong>{{ roleLabel(role) }}</strong>
                <small>{{ roleHint(role) }}</small>
              </span>
            </label>
          </div>
          <p v-if="errors.roles" id="user-roles-error" class="field-error">{{ errors.roles }}</p>
        </fieldset>

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
            {{ saving ? 'Saving…' : isEditing ? 'Save changes' : 'Create user' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
