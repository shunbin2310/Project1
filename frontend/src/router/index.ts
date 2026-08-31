import { createRouter, createWebHistory } from 'vue-router'

import { pinia } from '@/stores'
import { useAuthStore } from '@/stores/auth'
import { applicationRoles, type ApplicationRole } from '@/types/auth'

const adminRoutes: readonly ApplicationRole[] = [applicationRoles.admin]

function defaultAuthenticatedPath(roles: readonly string[]) {
  return roles.includes(applicationRoles.admin) ? '/departments' : '/my-tasks'
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
      path: '/suppliers',
      name: 'suppliers',
      component: () => import('@/views/suppliers/SupplierListView.vue'),
      meta: { title: 'Suppliers', requiresAuth: true, roles: adminRoutes },
    },
    {
      path: '/product-categories',
      name: 'product-categories',
      component: () => import('@/views/product-categories/ProductCategoryListView.vue'),
      meta: { title: 'Product Categories', requiresAuth: true, roles: adminRoutes },
    },
    {
      path: '/units-of-measure',
      name: 'units-of-measure',
      component: () => import('@/views/units-of-measure/UnitOfMeasureListView.vue'),
      meta: { title: 'Units of Measure', requiresAuth: true, roles: adminRoutes },
    },
    {
      path: '/products',
      name: 'products',
      component: () => import('@/views/products/ProductListView.vue'),
      meta: { title: 'Products', requiresAuth: true, roles: adminRoutes },
    },
    {
      path: '/purchase-requests',
      name: 'purchase-requests',
      component: () => import('@/views/purchase-requests/PurchaseRequestListView.vue'),
      meta: { title: 'Purchase Requests', requiresAuth: true },
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
