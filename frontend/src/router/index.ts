import { createRouter, createWebHistory } from 'vue-router'

import { pinia } from '@/stores'
import { useAuthStore } from '@/stores/auth'
import { applicationRoles, type ApplicationRole } from '@/types/auth'

const adminRoutes: readonly ApplicationRole[] = [applicationRoles.admin]
const procurementRoutes: readonly ApplicationRole[] = [
  applicationRoles.admin,
  applicationRoles.procurementOfficer,
]
const emailRecordRoutes: readonly ApplicationRole[] = [
  applicationRoles.admin,
  applicationRoles.procurementOfficer,
]
const catalogRoutes: readonly ApplicationRole[] = [
  applicationRoles.admin,
  applicationRoles.catalogManager,
]
const purchaseOrderRoutes: readonly ApplicationRole[] = [
  applicationRoles.admin,
  applicationRoles.procurementOfficer,
  applicationRoles.warehouseOfficer,
]
const goodsReceiptRoutes: readonly ApplicationRole[] = [
  applicationRoles.admin,
  applicationRoles.procurementOfficer,
  applicationRoles.warehouseOfficer,
]
const inventoryRoutes: readonly ApplicationRole[] = [
  applicationRoles.admin,
  applicationRoles.procurementOfficer,
  applicationRoles.warehouseOfficer,
]

function defaultAuthenticatedPath(roles: readonly string[]) {
  if (roles.includes(applicationRoles.admin)) return '/departments'
  if (roles.includes(applicationRoles.procurementOfficer)) return '/quotations'
  if (roles.includes(applicationRoles.warehouseOfficer)) return '/goods-receipts'
  if (roles.includes(applicationRoles.catalogManager)) return '/products'
  return '/my-tasks'
}

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: { template: '<span class="sr-only">Loading workspace</span>' },
      meta: { requiresAuth: true },
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/auth/LoginView.vue'),
      meta: { title: 'Sign in', guestOnly: true, layout: 'auth' },
    },
    {
      path: '/access-denied',
      name: 'access-denied',
      component: () => import('@/views/auth/AccessDeniedView.vue'),
      meta: { title: 'Access denied', requiresAuth: true, layout: 'auth' },
    },
    {
      path: '/my-tasks',
      name: 'my-tasks',
      component: () => import('@/views/tasks/MyTaskListView.vue'),
      meta: { title: 'My Tasks', requiresAuth: true },
    },
    {
      path: '/departments',
      name: 'departments',
      component: () => import('@/views/departments/DepartmentListView.vue'),
      meta: { title: 'Departments', requiresAuth: true, roles: adminRoutes },
    },
    {
      path: '/users',
      name: 'users',
      component: () => import('@/views/users/UserListView.vue'),
      meta: { title: 'Users', requiresAuth: true, roles: adminRoutes },
    },
    {
      path: '/workflow-templates',
      name: 'workflow-templates',
      component: () => import('@/views/workflow-templates/WorkflowTemplateListView.vue'),
      meta: { title: 'Workflow Templates', requiresAuth: true, roles: adminRoutes },
    },
    {
      path: '/email-templates',
      name: 'email-templates',
      component: () => import('@/views/email-templates/EmailTemplateListView.vue'),
      meta: { title: 'Email Templates', requiresAuth: true, roles: adminRoutes },
    },
    {
      path: '/suppliers',
      name: 'suppliers',
      component: () => import('@/views/suppliers/SupplierListView.vue'),
      meta: { title: 'Suppliers', requiresAuth: true, roles: procurementRoutes },
    },
    {
      path: '/supplier-products',
      name: 'supplier-products',
      component: () => import('@/views/supplier-products/SupplierProductListView.vue'),
      meta: { title: 'Supplier Products', requiresAuth: true, roles: procurementRoutes },
    },
    {
      path: '/product-categories',
      name: 'product-categories',
      component: () => import('@/views/product-categories/ProductCategoryListView.vue'),
      meta: { title: 'Product Categories', requiresAuth: true, roles: catalogRoutes },
    },
    {
      path: '/units-of-measure',
      name: 'units-of-measure',
      component: () => import('@/views/units-of-measure/UnitOfMeasureListView.vue'),
      meta: { title: 'Units of Measure', requiresAuth: true, roles: catalogRoutes },
    },
    {
      path: '/products',
      name: 'products',
      component: () => import('@/views/products/ProductListView.vue'),
      meta: { title: 'Products', requiresAuth: true, roles: catalogRoutes },
    },
    {
      path: '/purchase-requests',
      name: 'purchase-requests',
      component: () => import('@/views/purchase-requests/PurchaseRequestListView.vue'),
      meta: { title: 'Purchase Requests', requiresAuth: true },
    },
    {
      path: '/quotations',
      name: 'quotations',
      component: () => import('@/views/quotations/QuotationListView.vue'),
      meta: { title: 'Supplier Quotations', requiresAuth: true, roles: procurementRoutes },
    },
    {
      path: '/purchase-orders',
      name: 'purchase-orders',
      component: () => import('@/views/purchase-orders/PurchaseOrderListView.vue'),
      meta: { title: 'Purchase Orders', requiresAuth: true, roles: purchaseOrderRoutes },
    },
    {
      path: '/email-records',
      name: 'email-records',
      component: () => import('@/views/email-records/EmailRecordListView.vue'),
      meta: { title: 'Email Records', requiresAuth: true, roles: emailRecordRoutes },
    },
    {
      path: '/goods-receipts',
      name: 'goods-receipts',
      component: () => import('@/views/goods-receipts/GoodsReceiptListView.vue'),
      meta: { title: 'Goods Receiving', requiresAuth: true, roles: goodsReceiptRoutes },
    },
    {
      path: '/inventory',
      name: 'inventory',
      component: () => import('@/views/inventory/InventoryListView.vue'),
      meta: { title: 'Inventory', requiresAuth: true, roles: inventoryRoutes },
    },
  ],
})

router.beforeEach((to) => {
  const authStore = useAuthStore(pinia)

  if (to.meta.guestOnly && authStore.isAuthenticated) {
    return defaultAuthenticatedPath(authStore.roles)
  }

  if (to.meta.requiresAuth && !authStore.isAuthenticated) {
    return {
      name: 'login',
      query: to.fullPath === '/' ? {} : { redirect: to.fullPath },
    }
  }

  if (to.name === 'home') return defaultAuthenticatedPath(authStore.roles)

  const requiredRoles = (to.meta.roles ?? []) as readonly ApplicationRole[]
  if (requiredRoles.length && !authStore.hasAnyRole(requiredRoles)) {
    return { name: 'access-denied' }
  }
})

router.afterEach((to) => {
  const pageTitle = typeof to.meta.title === 'string' ? to.meta.title : 'Workspace'
  document.title = `${pageTitle} | Purchase & Inventory`
})

export default router
