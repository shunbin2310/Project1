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
  ],
})

router.afterEach((to) => {
  const pageTitle = typeof to.meta.title === 'string' ? to.meta.title : 'Workspace'
  document.title = `${pageTitle} | Purchase & Inventory`
})

export default router
