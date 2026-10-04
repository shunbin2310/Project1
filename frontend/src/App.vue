<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '@/stores/auth'
import { applicationRoles } from '@/types/auth'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const pageTitle = computed(() =>
  typeof route.meta.title === 'string' ? route.meta.title : 'Workspace',
)
const usesAuthLayout = computed(() => route.meta.layout === 'auth')
const isAdmin = computed(() => authStore.roles.includes(applicationRoles.admin))
const canManageProcurement = computed(
  () => isAdmin.value || authStore.roles.includes(applicationRoles.procurementOfficer),
)
const canManageCatalog = computed(
  () => isAdmin.value || authStore.roles.includes(applicationRoles.catalogManager),
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
  authStore.logout()
  await router.replace({ name: 'login' })
}
</script>

<template>
  <RouterView v-if="usesAuthLayout" />

  <div v-else class="app-shell">
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
        <RouterLink to="/dashboard">
          <span class="nav-icon" aria-hidden="true">DB</span>
          <span>Dashboard</span>
        </RouterLink>

        <p>Tasks</p>
        <RouterLink to="/my-tasks">
          <span class="nav-icon" aria-hidden="true">TK</span>
          <span>My Tasks</span>
        </RouterLink>

        <template v-if="isAdmin">
          <p>Administration</p>
          <RouterLink to="/departments">
            <span class="nav-icon" aria-hidden="true">DP</span>
            <span>Departments</span>
          </RouterLink>
          <RouterLink to="/users">
            <span class="nav-icon" aria-hidden="true">US</span>
            <span>Users</span>
          </RouterLink>
          <RouterLink to="/workflow-templates">
            <span class="nav-icon" aria-hidden="true">WF</span>
            <span>Workflow Templates</span>
          </RouterLink>
          <RouterLink to="/email-templates">
            <span class="nav-icon" aria-hidden="true">ET</span>
            <span>Email Templates</span>
          </RouterLink>
        </template>

        <template v-if="canManageProcurement">
          <p>Procurement settings</p>
          <RouterLink to="/suppliers">
            <span class="nav-icon" aria-hidden="true">SP</span>
            <span>Suppliers</span>
          </RouterLink>
          <RouterLink to="/supplier-products">
            <span class="nav-icon" aria-hidden="true">LK</span>
            <span>Supplier Products</span>
          </RouterLink>
        </template>

        <template v-if="canManageCatalog">
          <p>Catalog</p>
          <RouterLink to="/product-categories">
            <span class="nav-icon" aria-hidden="true">PC</span>
            <span>Product Categories</span>
          </RouterLink>
          <RouterLink to="/units-of-measure">
            <span class="nav-icon" aria-hidden="true">UM</span>
            <span>Units of Measure</span>
          </RouterLink>
          <RouterLink to="/products">
            <span class="nav-icon" aria-hidden="true">PR</span>
            <span>Products</span>
          </RouterLink>
        </template>

        <p>Purchasing</p>
        <RouterLink v-if="canViewPurchaseRequests" to="/purchase-requests">
          <span class="nav-icon" aria-hidden="true">RQ</span>
          <span>Purchase Requests</span>
        </RouterLink>
        <RouterLink v-if="canManageProcurement" to="/quotations">
          <span class="nav-icon" aria-hidden="true">QT</span>
          <span>Supplier Quotations</span>
        </RouterLink>
        <RouterLink v-if="canViewPurchaseOrders" to="/purchase-orders">
          <span class="nav-icon" aria-hidden="true">PO</span>
          <span>Purchase Orders</span>
        </RouterLink>
        <RouterLink v-if="canManageProcurement" to="/email-records">
          <span class="nav-icon" aria-hidden="true">EM</span>
          <span>Email Records</span>
        </RouterLink>
        <RouterLink v-if="canViewGoodsReceipts" to="/goods-receipts">
          <span class="nav-icon" aria-hidden="true">GR</span>
          <span>Goods Receiving</span>
        </RouterLink>

        <template v-if="canViewInventory">
          <p>Stock</p>
          <RouterLink to="/inventory">
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
          <button class="logout-button" type="button" @click="logout">Sign out</button>
        </div>
      </header>

      <main class="page-content">
        <RouterView />
      </main>
    </div>
  </div>
</template>
