<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

import { useAuthStore } from '@/stores/auth'
import { applicationRoles } from '@/types/auth'

const authStore = useAuthStore()
const returnPath = computed(() =>
  authStore.roles.includes(applicationRoles.admin) ? '/departments' : '/my-tasks',
)
</script>

<template>
  <main class="status-page">
    <section class="status-card">
      <div class="status-code">403</div>
      <p class="eyebrow">Access denied</p>
      <h1>You do not have permission to open this page.</h1>
      <p>
        You are signed in as <strong>{{ authStore.user?.fullName }}</strong
        >. Your current role does not include access to this administration area.
      </p>
      <RouterLink class="button button-primary" :to="returnPath">Return to workspace</RouterLink>
    </section>
  </main>
</template>
