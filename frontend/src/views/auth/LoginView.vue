<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '@/stores/auth'
import { applicationRoles } from '@/types/auth'

const demoPassword = import.meta.env.VITE_DEMO_PASSWORD ?? 'Project1Demo123!'
const demoAccounts = [
  {
    email: 'requester@demo.local',
    name: 'Demo Requester',
    role: applicationRoles.requester,
    description: 'Create and submit purchase requests',
  },
  {
    email: 'department@demo.local',
    name: 'Department Approver',
    role: applicationRoles.departmentApprover,
    description: 'Review requests for the department',
  },
  {
    email: 'finance@demo.local',
    name: 'Finance Approver',
    role: applicationRoles.financeApprover,
    description: 'Confirm budget and final approval',
  },
  {
    email: 'admin@demo.local',
    name: 'Demo Admin',
    role: applicationRoles.admin,
    description: 'Manage master data and all requests',
  },
] as const

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const form = reactive({ email: '', password: '' })
const errorMessage = ref('')
const selectedDemoEmail = ref('')
const sessionExpired = computed(() => route.query.reason === 'session-expired')

async function submit() {
  errorMessage.value = ''

  if (!form.email.trim() || !form.password) {
    errorMessage.value = 'Enter both email and password.'
    return
  }

  try {
    const user = await authStore.login({
      email: form.email.trim(),
      password: form.password,
    })
    const requestedPath = typeof route.query.redirect === 'string' ? route.query.redirect : ''
    const safeRedirect = requestedPath.startsWith('/') && !requestedPath.startsWith('//')
    const defaultPath = user.roles.includes(applicationRoles.admin) ? '/departments' : '/my-tasks'

    await router.replace(safeRedirect ? requestedPath : defaultPath)
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Unable to sign in.'
  } finally {
    selectedDemoEmail.value = ''
  }
}

async function loginAsDemo(email: string) {
  selectedDemoEmail.value = email
  form.email = email
  form.password = demoPassword
  await submit()
}
</script>

<template>
  <main class="auth-page">
    <section class="auth-introduction">
      <div class="auth-brand">
        <span class="brand-mark" aria-hidden="true">PI</span>
        <div>
          <strong>Purchase & Inventory</strong>
          <small>Workflow-enabled operations workspace</small>
        </div>
      </div>

      <div class="auth-copy">
        <p class="eyebrow">Portfolio demonstration</p>
        <h1>Sign in to the procurement workspace</h1>
        <p>
          Explore role-based purchasing, multi-stage approvals, product catalog management, and a
          complete workflow audit trail.
        </p>
      </div>

      <ul class="auth-feature-list" aria-label="Application features">
        <li><span>01</span>Role-based access control</li>
        <li><span>02</span>Template and instance workflow engine</li>
        <li><span>03</span>Purchase request approval history</li>
      </ul>
    </section>

    <section class="login-panel" aria-labelledby="login-title">
      <div class="login-card">
        <header>
          <p class="eyebrow">Secure access</p>
          <h2 id="login-title">Welcome back</h2>
          <p>Use your account or choose a demo role below.</p>
        </header>

        <div v-if="sessionExpired" class="alert alert-error" role="alert">
          Your session expired. Please sign in again.
        </div>

        <div v-if="errorMessage" class="alert alert-error" role="alert">
          {{ errorMessage }}
        </div>

        <form class="login-form" novalidate @submit.prevent="submit">
          <div class="form-field">
            <label for="login-email">Email</label>
            <input
              id="login-email"
              v-model="form.email"
              type="email"
              autocomplete="username"
              placeholder="name@example.com"
            />
          </div>

          <div class="form-field">
            <label for="login-password">Password</label>
            <input
              id="login-password"
              v-model="form.password"
              type="password"
              autocomplete="current-password"
              placeholder="Enter your password"
            />
          </div>

          <button
            class="button button-primary login-submit"
            type="submit"
            :disabled="authStore.loggingIn"
          >
            {{ authStore.loggingIn && !selectedDemoEmail ? 'Signing in...' : 'Sign in' }}
          </button>
        </form>

        <div class="demo-section">
          <div class="demo-heading">
            <span>Demo accounts</span>
            <small>One-click access for interview review</small>
          </div>

          <div class="demo-account-grid">
            <button
              v-for="account in demoAccounts"
              :key="account.email"
              class="demo-account"
              type="button"
              :disabled="authStore.loggingIn"
              @click="loginAsDemo(account.email)"
            >
              <span>{{ account.role.replace(/_.*/, '').slice(0, 2) }}</span>
              <div>
                <strong>{{ account.name }}</strong>
                <small>{{ account.description }}</small>
              </div>
              <em>{{ selectedDemoEmail === account.email ? 'Signing in...' : 'Use account' }}</em>
            </button>
          </div>
        </div>
      </div>
    </section>
  </main>
</template>
