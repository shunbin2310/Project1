<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'

import { dashboardService } from '@/services/dashboardService'
import { useAuthStore } from '@/stores/auth'
import type { Dashboard } from '@/types/dashboard'

const authStore = useAuthStore()
const dashboard = ref<Dashboard | null>(null)
const loading = ref(true)
const loadError = ref('')

const firstName = computed(() => authStore.user?.fullName.split(/\s+/)[0] ?? 'there')
const roleDescription = computed(() => {
  const labels = authStore.roles.map((role) =>
    role
      .toLowerCase()
      .split('_')
      .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
      .join(' '),
  )
  return labels.join(' · ')
})

onMounted(loadDashboard)

async function loadDashboard() {
  loading.value = true
  loadError.value = ''

  try {
    dashboard.value = await dashboardService.get()
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : 'Unable to load the dashboard.'
  } finally {
    loading.value = false
  }
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}
</script>

<template>
  <section class="page-section dashboard-page">
    <header class="page-heading dashboard-heading">
      <div>
        <p class="eyebrow">Operations overview</p>
        <h1>Welcome back, {{ firstName }}</h1>
        <p class="page-description">
          Your role-aware summary, reminders, and latest business activity in one place.
        </p>
        <p class="dashboard-role-label">{{ roleDescription }}</p>
      </div>

      <button class="button button-secondary" type="button" :disabled="loading" @click="loadDashboard">
        {{ loading ? 'Refreshing...' : 'Refresh dashboard' }}
      </button>
    </header>

    <section v-if="loading" class="data-panel dashboard-state" aria-live="polite">
      <div class="spinner" aria-hidden="true"></div>
      <strong>Preparing your dashboard</strong>
      <p>Calculating current work and recent activity for your roles.</p>
    </section>

    <section v-else-if="loadError" class="data-panel dashboard-state panel-state-error" role="alert">
      <div class="empty-icon" aria-hidden="true">!</div>
      <strong>Dashboard could not be loaded</strong>
      <p>{{ loadError }}</p>
      <button class="button button-secondary" type="button" @click="loadDashboard">Try again</button>
    </section>

    <template v-else-if="dashboard">
      <section class="dashboard-summary-grid" aria-label="Role summary">
        <RouterLink
          v-for="card in dashboard.summaryCards"
          :key="card.key"
          :to="card.route"
          class="dashboard-summary-card"
          :class="`dashboard-summary-${card.tone}`"
        >
          <span class="summary-label">{{ card.label }}</span>
          <strong>{{ card.value }}</strong>
          <span>{{ card.description }}</span>
          <em>Open module <span aria-hidden="true">→</span></em>
        </RouterLink>
      </section>

      <div class="dashboard-content-grid">
        <section class="data-panel dashboard-panel" aria-labelledby="dashboard-reminders-title">
          <div class="panel-toolbar">
            <div>
              <h2 id="dashboard-reminders-title">Reminders</h2>
              <p>Live items that currently need attention</p>
            </div>
            <span class="dashboard-panel-count">{{ dashboard.reminders.length }}</span>
          </div>

          <div v-if="dashboard.reminders.length === 0" class="dashboard-empty-state">
            <div class="empty-icon" aria-hidden="true">OK</div>
            <div>
              <strong>Nothing needs attention</strong>
              <p>Your current role has no outstanding reminders.</p>
            </div>
          </div>

          <div v-else class="dashboard-reminder-list">
            <RouterLink
              v-for="reminder in dashboard.reminders"
              :key="reminder.key"
              :to="reminder.route"
              class="dashboard-reminder"
              :class="`dashboard-reminder-${reminder.severity}`"
            >
              <span class="dashboard-reminder-count">{{ reminder.count }}</span>
              <span class="dashboard-reminder-copy">
                <strong>{{ reminder.title }}</strong>
                <small>{{ reminder.description }}</small>
              </span>
              <span class="dashboard-reminder-arrow" aria-hidden="true">→</span>
            </RouterLink>
          </div>
        </section>

        <section class="data-panel dashboard-panel" aria-labelledby="dashboard-activity-title">
          <div class="panel-toolbar">
            <div>
              <h2 id="dashboard-activity-title">Recent activity</h2>
              <p>Latest records available to your roles</p>
            </div>
            <span class="dashboard-panel-count">{{ dashboard.recentActivity.length }}</span>
          </div>

          <div v-if="dashboard.recentActivity.length === 0" class="dashboard-empty-state">
            <div class="empty-icon" aria-hidden="true">—</div>
            <div>
              <strong>No recent activity</strong>
              <p>New business activity will appear here.</p>
            </div>
          </div>

          <ol v-else class="dashboard-activity-list">
            <li v-for="activity in dashboard.recentActivity" :key="activity.key">
              <span class="dashboard-activity-dot" aria-hidden="true"></span>
              <RouterLink :to="activity.route" class="dashboard-activity-link">
                <span class="dashboard-activity-meta">
                  <em>{{ activity.module }}</em>
                  <strong>{{ activity.reference }}</strong>
                </span>
                <span class="dashboard-activity-copy">
                  <strong>{{ activity.title }}</strong>
                  <small>{{ activity.description }}</small>
                  <span>{{ activity.actorName }} · {{ formatDateTime(activity.occurredAtUtc) }}</span>
                </span>
                <span class="dashboard-reminder-arrow" aria-hidden="true">→</span>
              </RouterLink>
            </li>
          </ol>
        </section>
      </div>

      <p class="dashboard-generated-time">
        Live data generated {{ formatDateTime(dashboard.generatedAtUtc) }}
      </p>
    </template>
  </section>
</template>
