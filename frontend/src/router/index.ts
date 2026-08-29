import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      redirect: '/departments',
    },
    {
      path: '/departments',
      name: 'departments',
      component: () => import('@/views/departments/DepartmentListView.vue'),
      meta: { title: 'Departments' },
    },
    {
      path: '/suppliers',
      name: 'suppliers',
      component: () => import('@/views/suppliers/SupplierListView.vue'),
      meta: { title: 'Suppliers' },
    },
    {
      path: '/product-categories',
      name: 'product-categories',
      component: () => import('@/views/product-categories/ProductCategoryListView.vue'),
      meta: { title: 'Product Categories' },
    },
    {
      path: '/units-of-measure',
      name: 'units-of-measure',
      component: () => import('@/views/units-of-measure/UnitOfMeasureListView.vue'),
      meta: { title: 'Units of Measure' },
    },
    {
      path: '/products',
      name: 'products',
      component: () => import('@/views/products/ProductListView.vue'),
      meta: { title: 'Products' },
    },
  ],
})

router.afterEach((to) => {
  const pageTitle = typeof to.meta.title === 'string' ? to.meta.title : 'Workspace'
  document.title = `${pageTitle} | Purchase & Inventory`
})

export default router
