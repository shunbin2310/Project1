<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import UserForm from '@/components/users/UserForm.vue'
import { departmentService } from '@/services/departmentService'
import { ApiError, userService } from '@/services/userService'
import { useAuthStore } from '@/stores/auth'
import type { ApplicationRole } from '@/types/auth'
import type { Department } from '@/types/department'
import type { User, UserFormValues } from '@/types/user'

type StatusFilter = 'all' | 'active' | 'inactive'

const router = useRouter()
const authStore = useAuthStore()
const users = ref<User[]>([])
const departments = ref<Department[]>([])
const availableRoles = ref<ApplicationRole[]>([])
const loading = ref(true)
const saving = ref(false)
const search = ref('')
const statusFilter = ref<StatusFilter>('all')
const loadError = ref('')
const operationError = ref('')
const formError = ref('')
const successMessage = ref('')
const formOpen = ref(false)
const editingUser = ref<User | null>(null)

const currentUserId = computed(() => authStore.user?.id ?? null)
const activeCount = computed(() => users.value.filter((user) => user.isActive).length)
const inactiveCount = computed(() => users.value.length - activeCount.value)
const visibleUsers = computed(() => {
  const term = search.value.trim().toLowerCase()

  return users.value.filter((user) => {
    if (statusFilter.value === 'active' && !user.isActive) return false
    if (statusFilter.value === 'inactive' && user.isActive) return false
    if (!term) return true

    return [
      user.email,
      user.fullName,
      user.departmentCode ?? '',
      user.departmentName ?? '',
      ...user.roles,
    ].some((value) => value.toLowerCase().includes(term))
  })
})

onMounted(loadPage)

async function loadPage() {
  loading.value = true
  loadError.value = ''

  try {
    const [userRecords, roleRecords, departmentRecords] = await Promise.all([
      userService.getAll(true),
      userService.getRoles(),
      departmentService.getAll(false),
    ])
    users.value = userRecords
    availableRoles.value = roleRecords
    departments.value = departmentRecords
  } catch (error) {
    loadError.value = getErrorMessage(error, 'Unable to load user administration data.')
  } finally {
    loading.value = false
  }
}

async function loadUsers() {
  users.value = await userService.getAll(true)
}

function openCreateForm() {
  editingUser.value = null
  formError.value = ''
  formOpen.value = true
}

function openEditForm(user: User) {
  editingUser.value = user
  formError.value = ''
  formOpen.value = true
}

function closeForm() {
  if (saving.value) return
  formOpen.value = false
  editingUser.value = null
  formError.value = ''
}

async function saveUser(values: UserFormValues) {
  saving.value = true
  formError.value = ''

  try {
    if (editingUser.value) {
      const userId = editingUser.value.id
      await userService.update(userId, {
        fullName: values.fullName,
        departmentId: values.departmentId,
        roles: values.roles,
      })
      formOpen.value = false
      editingUser.value = null

      if (userId === currentUserId.value) {
        await logoutAfterOwnAccountChange('Your account was updated. Please sign in again.')
        return
      }

      showSuccess(`${values.fullName} was updated successfully.`)
    } else {
      await userService.create({
        email: values.email,
        fullName: values.fullName,
        departmentId: values.departmentId,
        roles: values.roles,
      })
      formOpen.value = false
      showSuccess(`${values.fullName} was created successfully.`)
    }

    await loadUsers()
  } catch (error) {
    formError.value = getErrorMessage(error, 'Unable to save the user.')
  } finally {
    saving.value = false
  }
}

async function changeUserStatus(user: User) {
  if (user.id === currentUserId.value && user.isActive) return

  const action = user.isActive ? 'Deactivate' : 'Reactivate'
  const confirmed = window.confirm(
    `${action} ${user.fullName}? ${
      user.isActive
        ? 'They will be signed out and unable to log in.'
        : 'They will be able to log in again.'
    }`,
  )
  if (!confirmed) return

  operationError.value = ''
  try {
    await userService.setActive(user.id, !user.isActive)
    showSuccess(`${user.fullName} was ${user.isActive ? 'deactivated' : 'reactivated'}.`)
    await loadUsers()
  } catch (error) {
    operationError.value = getErrorMessage(error, `Unable to ${action.toLowerCase()} the user.`)
  }
}

async function logoutAfterOwnAccountChange(message: string) {
  window.alert(message)
  authStore.logout()
  await router.replace({ name: 'login' })
}

function roleLabel(role: string) {
  return role
    .toLowerCase()
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}

function initials(user: User) {
  return user.fullName
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join('')
}

function showSuccess(message: string) {
  successMessage.value = message
  window.setTimeout(() => {
    if (successMessage.value === message) successMessage.value = ''
  }, 3500)
}

function getErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ApiError || error instanceof Error) return error.message
  return fallback
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-MY', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  }).format(new Date(value))
}
</script>

<template>
  <section class="page-section">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Access administration</p>
        <h1>Users</h1>
        <p class="page-description">
          Manage user access, organization assignments, roles, and account status.
        </p>
      </div>
      <button class="button button-primary" type="button" @click="openCreateForm">
        <span aria-hidden="true">＋</span>
        New user
      </button>
    </header>

    <div class="summary-grid" aria-label="User summary">
      <article class="summary-card">
        <span class="summary-label">Total users</span>
        <strong>{{ users.length }}</strong>
        <span>All registered accounts</span>
      </article>
      <article class="summary-card summary-card-positive">
        <span class="summary-label">Active</span>
        <strong>{{ activeCount }}</strong>
        <span>Can access the workspace</span>
      </article>
      <article class="summary-card summary-card-muted">
        <span class="summary-label">Inactive</span>
        <strong>{{ inactiveCount }}</strong>
        <span>Access has been disabled</span>
      </article>
    </div>

    <Transition name="toast">
      <div v-if="successMessage" class="success-toast" role="status">
        <span class="success-toast-icon" aria-hidden="true">OK</span>
        <div>
          <strong>Operation completed</strong>
          <p>{{ successMessage }}</p>
        </div>
        <button type="button" aria-label="Dismiss success message" @click="successMessage = ''">
          ×
        </button>
      </div>
    </Transition>

    <div v-if="operationError" class="alert alert-error" role="alert">
      <span>{{ operationError }}</span>
      <button type="button" aria-label="Dismiss error" @click="operationError = ''">×</button>
    </div>

    <section class="data-panel" aria-labelledby="user-list-title">
      <div class="panel-toolbar">
        <div>
          <h2 id="user-list-title">User directory</h2>
          <p>{{ visibleUsers.length }} records shown</p>
        </div>

        <div class="toolbar-actions">
          <label class="search-control">
            <span class="sr-only">Search users</span>
            <span aria-hidden="true">⌕</span>
            <input
              v-model="search"
              type="search"
              placeholder="Search name, email, department, or role"
            />
          </label>
          <label class="step-filter-control">
            <span class="sr-only">Filter users by status</span>
            <select v-model="statusFilter">
              <option value="all">All users</option>
              <option value="active">Active</option>
              <option value="inactive">Inactive</option>
            </select>
          </label>
        </div>
      </div>

      <div v-if="loading" class="panel-state" aria-live="polite">
        <span class="spinner" aria-hidden="true"></span>
        <strong>Loading users</strong>
        <p>Retrieving account, department, and role information.</p>
      </div>

      <div v-else-if="loadError" class="panel-state panel-state-error">
        <strong>Users could not be loaded</strong>
        <p>{{ loadError }}</p>
        <button class="button button-secondary" type="button" @click="loadPage">Try again</button>
      </div>

      <div v-else-if="visibleUsers.length === 0" class="panel-state">
        <div class="empty-icon" aria-hidden="true">US</div>
        <strong>{{ search ? 'No matching users' : 'No users in this status' }}</strong>
        <p>Try another search term or status filter.</p>
      </div>

      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>User</th>
              <th>Department</th>
              <th>Roles</th>
              <th>Status</th>
              <th>Created</th>
              <th><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="user in visibleUsers" :key="user.id">
              <td>
                <div class="user-identity">
                  <span class="code-avatar">{{ initials(user) }}</span>
                  <span
                    ><strong>{{ user.fullName }}</strong
                    ><small>{{ user.email }}</small></span
                  >
                </div>
              </td>
              <td>
                <span v-if="user.departmentName" class="table-primary">{{
                  user.departmentName
                }}</span>
                <span v-else class="table-muted">No department</span>
                <small v-if="user.departmentCode" class="table-secondary">{{
                  user.departmentCode
                }}</small>
              </td>
              <td>
                <div class="role-badge-list">
                  <span v-for="role in user.roles" :key="role" class="role-badge">{{
                    roleLabel(role)
                  }}</span>
                </div>
              </td>
              <td>
                <span class="status-badge" :class="user.isActive ? 'is-active' : 'is-inactive'">
                  <span aria-hidden="true"></span>{{ user.isActive ? 'Active' : 'Inactive' }}
                </span>
              </td>
              <td>{{ formatDate(user.createdAtUtc) }}</td>
              <td>
                <div class="row-actions user-row-actions">
                  <button class="text-button" type="button" @click="openEditForm(user)">
                    Edit
                  </button>
                  <button
                    v-if="user.isActive"
                    class="text-button text-button-danger"
                    type="button"
                    :disabled="user.id === currentUserId"
                    :title="
                      user.id === currentUserId
                        ? 'You cannot deactivate your own account.'
                        : undefined
                    "
                    @click="changeUserStatus(user)"
                  >
                    Deactivate
                  </button>
                  <button
                    v-else
                    class="text-button text-button-positive"
                    type="button"
                    @click="changeUserStatus(user)"
                  >
                    Reactivate
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <UserForm
      v-if="formOpen"
      :user="editingUser"
      :current-user-id="currentUserId"
      :available-roles="availableRoles"
      :departments="departments"
      :saving="saving"
      :error-message="formError"
      @cancel="closeForm"
      @save="saveUser"
    />
  </section>
</template>
