<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'

const route = useRoute()
const pageTitle = computed(() =>
  typeof route.meta.title === 'string' ? route.meta.title : 'Workspace',
)
</script>

<template>
  <div class="app-shell">
    <aside class="sidebar">
      <div class="brand-block">
        <div class="brand-mark" aria-hidden="true">PI</div>
        <div>
          <strong>Purchase & Inventory</strong>
          <span>Operations workspace</span>
        </div>
      </div>

      <nav class="primary-nav" aria-label="Primary navigation">
        <p>Workspace</p>
        <RouterLink to="/departments">
          <span class="nav-icon" aria-hidden="true">DP</span>
          <span>Departments</span>
        </RouterLink>

        <p>Coming next</p>
        <span class="nav-placeholder">
          <span class="nav-icon" aria-hidden="true">PR</span>
          Purchase requests
        </span>
        <span class="nav-placeholder">
          <span class="nav-icon" aria-hidden="true">AP</span>
          Approvals
        </span>
        <span class="nav-placeholder">
          <span class="nav-icon" aria-hidden="true">IN</span>
          Inventory
        </span>
      </nav>

      <div class="sidebar-footer">
        <span class="environment-dot" aria-hidden="true"></span>
        <span>
          <strong>Development</strong>
          <small>Local environment</small>
        </span>
      </div>
    </aside>

    <div class="workspace">
      <header class="topbar">
        <div>
          <span>Organization</span>
          <strong>{{ pageTitle }}</strong>
        </div>
        <div class="profile-chip" aria-label="Current user">
          <span>AD</span>
          <div>
            <strong>Admin preview</strong>
            <small>System administrator</small>
          </div>
        </div>
      </header>

      <main class="page-content">
        <RouterView />
      </main>
    </div>
  </div>
</template>
