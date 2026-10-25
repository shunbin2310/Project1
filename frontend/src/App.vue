<script setup lang="ts">
import { computed, onUnmounted, ref, shallowRef } from 'vue'
import {
  RouterLink,
  RouterView,
  useRoute,
  useRouter,
  type RouteLocationNormalized,
} from 'vue-router'

import { useAuthStore } from '@/stores/auth'
import { applicationRoles, myTaskRoles } from '@/types/auth'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const pendingNavigation = shallowRef<RouteLocationNormalized | null>(null)
const loggingOut = ref(false)
const removeNavigationStart = router.beforeEach((to) => {
  pendingNavigation.value = to
})
const removeNavigationEnd = router.afterEach((to) => {
  if (pendingNavigation.value === to) pendingNavigation.value = null
})
const removeNavigationError = router.onError((_error, to) => {
  if (pendingNavigation.value === to) pendingNavigation.value = null
})
onUnmounted(() => {
  removeNavigationStart()
  removeNavigationEnd()
  removeNavigationError()
})
const pageTitle = computed(() =>
  typeof route.meta.title === 'string' ? route.meta.title : 'Workspace',
)
const usesAuthLayout = computed(() => route.meta.layout === 'auth')
const isAdmin = computed(() => authStore.roles.includes(applicationRoles.admin))
const canViewMyTasks = computed(() => authStore.hasAnyRole(myTaskRoles))
const canManageProcurement = computed(
  () => isAdmin.value || authStore.roles.includes(applicationRoles.procurementOfficer),
)
const canManageWarehouse = computed(
  () => isAdmin.value || authStore.roles.includes(applicationRoles.warehouseOfficer),
)
const canViewPurchaseRequests = computed(
  () =>
    isAdmin.value ||
    authStore.roles.some((role) => role !== applicationRoles.purchaseOrderApprover),
)
const canViewPurchaseOrders = computed(
  () =>
    canManageProcurement.value ||
    canManageWarehouse.value ||
    authStore.roles.includes(applicationRoles.purchaseOrderApprover),
)
const canViewGoodsReceipts = computed(() => canManageProcurement.value || canManageWarehouse.value)
const canViewInventory = computed(() => canManageProcurement.value || canManageWarehouse.value)
const initials = computed(() =>
  (authStore.user?.fullName ?? 'User')
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join(''),
)
const roleLabel = computed(() =>
  (authStore.roles[0] ?? 'User')
    .toLowerCase()
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' '),
)

async function logout() {
  if (loggingOut.value) return
  loggingOut.value = true
  try {
    authStore.logout()
    await router.replace({ name: 'login' })
  } finally {
    loggingOut.value = false
  }
}
</script>

<template>
  <RouterView v-if="usesAuthLayout" />

  <div v-else class="app-shell">
    <aside class="sidebar">
      <div class="brand-block">
        <div class="brand-mark" aria-hidden="true">PI</div>
        <div>
          <strong>Project1 Inventory</strong>
          <span>Operations workspace</span>
        </div>
      </div>

      <nav class="primary-nav" aria-label="Primary navigation">
        <span class="sr-only" role="status">{{ pendingNavigation ? 'Loading page…' : '' }}</span>
        <p>Workspace</p>
        <RouterLink to="/dashboard" :aria-busy="pendingNavigation?.path === '/dashboard'">
          <span class="nav-icon" aria-hidden="true">DB</span>
          <span>Dashboard</span>
        </RouterLink>

        <template v-if="canViewMyTasks">
          <p>Tasks</p>
          <RouterLink to="/my-tasks" :aria-busy="pendingNavigation?.path === '/my-tasks'">
            <span class="nav-icon" aria-hidden="true">TK</span>
            <span>My Tasks</span>
          </RouterLink>
        </template>

        <template v-if="isAdmin">
          <p>Administration</p>
          <RouterLink to="/departments" :aria-busy="pendingNavigation?.path === '/departments'">
            <span class="nav-icon" aria-hidden="true">DP</span>
            <span>Departments</span>
          </RouterLink>
          <RouterLink to="/users" :aria-busy="pendingNavigation?.path === '/users'">
            <span class="nav-icon" aria-hidden="true">US</span>
            <span>Users</span>
          </RouterLink>
          <RouterLink
            to="/workflow-templates"
            :aria-busy="pendingNavigation?.path === '/workflow-templates'"
          >
            <span class="nav-icon" aria-hidden="true">WF</span>
            <span>Workflow Templates</span>
          </RouterLink>
          <RouterLink
            to="/email-templates"
            :aria-busy="pendingNavigation?.path === '/email-templates'"
          >
            <span class="nav-icon" aria-hidden="true">ET</span>
            <span>Email Templates</span>
          </RouterLink>
        </template>

        <template v-if="canManageProcurement">
          <p>Procurement settings</p>
          <RouterLink to="/suppliers" :aria-busy="pendingNavigation?.path === '/suppliers'">
            <span class="nav-icon" aria-hidden="true">SP</span>
            <span>Suppliers</span>
          </RouterLink>
          <RouterLink
            to="/supplier-products"
            :aria-busy="pendingNavigation?.path === '/supplier-products'"
          >
            <span class="nav-icon" aria-hidden="true">LK</span>
            <span>Supplier Products</span>
          </RouterLink>
          <RouterLink to="/products" :aria-busy="pendingNavigation?.path === '/products'">
            <span class="nav-icon" aria-hidden="true">PR</span>
            <span>Products</span>
          </RouterLink>
        </template>

        <p>Purchasing</p>
        <RouterLink
          v-if="canViewPurchaseRequests"
          to="/purchase-requests"
          :aria-busy="pendingNavigation?.path === '/purchase-requests'"
        >
          <span class="nav-icon" aria-hidden="true">RQ</span>
          <span>Purchase Requests</span>
        </RouterLink>
        <RouterLink
          v-if="canManageProcurement"
          to="/quotations"
          :aria-busy="pendingNavigation?.path === '/quotations'"
        >
          <span class="nav-icon" aria-hidden="true">QT</span>
          <span>Supplier Quotations</span>
        </RouterLink>
        <RouterLink
          v-if="canViewPurchaseOrders"
          to="/purchase-orders"
          :aria-busy="pendingNavigation?.path === '/purchase-orders'"
        >
          <span class="nav-icon" aria-hidden="true">PO</span>
          <span>Purchase Orders</span>
        </RouterLink>
        <RouterLink
          v-if="canManageProcurement"
          to="/email-records"
          :aria-busy="pendingNavigation?.path === '/email-records'"
        >
          <span class="nav-icon" aria-hidden="true">EM</span>
          <span>Email Records</span>
        </RouterLink>
        <RouterLink
          v-if="canViewGoodsReceipts"
          to="/goods-receipts"
          :aria-busy="pendingNavigation?.path === '/goods-receipts'"
        >
          <span class="nav-icon" aria-hidden="true">GR</span>
          <span>Goods Receiving</span>
        </RouterLink>

        <template v-if="canViewInventory">
          <p>Stock</p>
          <RouterLink to="/inventory" :aria-busy="pendingNavigation?.path === '/inventory'">
            <span class="nav-icon" aria-hidden="true">IN</span>
            <span>Inventory</span>
          </RouterLink>
        </template>
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
        <div class="profile-chip">
          <span aria-hidden="true">{{ initials }}</span>
          <div>
            <strong>{{ authStore.user?.fullName }}</strong>
            <small>{{ roleLabel }}</small>
          </div>
          <button
            class="logout-button"
            type="button"
            :disabled="loggingOut"
            :aria-busy="loggingOut"
            @click="logout"
          >
            Sign out
          </button>
        </div>
      </header>

      <main class="page-content">
        <RouterView />
      </main>
    </div>
  </div>
</template>
