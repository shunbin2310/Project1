import { afterEach, describe, expect, it } from 'vitest'

import router from '@/router'
import { pinia } from '@/stores'
import { useAuthStore } from '@/stores/auth'
import type { ApplicationRole } from '@/types/auth'

function authenticate(role: ApplicationRole) {
  const authStore = useAuthStore(pinia)
  authStore.$patch({
    session: {
      accessToken: 'test-token',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      user: {
        id: role === 'ADMIN' ? 4 : 1,
        email: 'user@demo.local',
        fullName: 'Demo User',
        departmentId: 1,
        departmentCode: 'IT',
        departmentName: 'Information Technology',
        roles: [role],
      },
    },
  })
}

describe('router authentication guards', () => {
  afterEach(async () => {
    useAuthStore(pinia).logout()
    await router.replace('/login')
  })

  it('redirects unauthenticated users to login', async () => {
    useAuthStore(pinia).logout()

    await router.push('/purchase-requests')

    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/purchase-requests')
  })

  it('redirects a requester away from admin routes', async () => {
    authenticate('REQUESTER')

    await router.push('/departments')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('redirects a requester away from user administration', async () => {
    authenticate('REQUESTER')

    await router.push('/users')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('redirects a requester away from workflow template administration', async () => {
    authenticate('REQUESTER')

    await router.push('/workflow-templates')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('redirects a requester away from email template administration', async () => {
    authenticate('REQUESTER')

    await router.push('/email-templates')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('redirects a requester away from supplier product administration', async () => {
    authenticate('REQUESTER')

    await router.push('/supplier-products')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('redirects a requester away from supplier quotations', async () => {
    authenticate('REQUESTER')

    await router.push('/quotations')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('redirects a requester away from purchase orders', async () => {
    authenticate('REQUESTER')

    await router.push('/purchase-orders')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('redirects a requester away from email records', async () => {
    authenticate('REQUESTER')

    await router.push('/email-records')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('redirects a requester away from goods receiving', async () => {
    authenticate('REQUESTER')

    await router.push('/goods-receipts')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('redirects a requester away from inventory', async () => {
    authenticate('REQUESTER')

    await router.push('/inventory')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('uses My Tasks as the requester default page', async () => {
    authenticate('REQUESTER')

    await router.push('/')

    expect(router.currentRoute.value.name).toBe('my-tasks')
  })

  it('allows an administrator to open admin routes', async () => {
    authenticate('ADMIN')

    await router.push('/departments')

    expect(router.currentRoute.value.name).toBe('departments')
  })

  it('allows an administrator to open user administration', async () => {
    authenticate('ADMIN')

    await router.push('/users')

    expect(router.currentRoute.value.name).toBe('users')
  })

  it('allows an administrator to open workflow template administration', async () => {
    authenticate('ADMIN')

    await router.push('/workflow-templates')

    expect(router.currentRoute.value.name).toBe('workflow-templates')
  })

  it('allows an administrator to open email template administration', async () => {
    authenticate('ADMIN')

    await router.push('/email-templates')

    expect(router.currentRoute.value.name).toBe('email-templates')
  })

  it('allows an administrator to open supplier product administration', async () => {
    authenticate('ADMIN')

    await router.push('/supplier-products')

    expect(router.currentRoute.value.name).toBe('supplier-products')
  })

  it('allows an administrator to open supplier quotations', async () => {
    authenticate('ADMIN')

    await router.push('/quotations')

    expect(router.currentRoute.value.name).toBe('quotations')
  })

  it('allows an administrator to open purchase orders', async () => {
    authenticate('ADMIN')

    await router.push('/purchase-orders')

    expect(router.currentRoute.value.name).toBe('purchase-orders')
  })

  it('allows an administrator to open email records', async () => {
    authenticate('ADMIN')

    await router.push('/email-records')

    expect(router.currentRoute.value.name).toBe('email-records')
  })

  it('allows an administrator to open goods receiving', async () => {
    authenticate('ADMIN')

    await router.push('/goods-receipts')

    expect(router.currentRoute.value.name).toBe('goods-receipts')
  })

  it('allows an administrator to open inventory', async () => {
    authenticate('ADMIN')

    await router.push('/inventory')

    expect(router.currentRoute.value.name).toBe('inventory')
  })

  it('allows every authenticated role to open My Tasks', async () => {
    authenticate('FINANCE_APPROVER')

    await router.push('/my-tasks')

    expect(router.currentRoute.value.name).toBe('my-tasks')
  })

  it('routes procurement officers to quotations and purchasing pages', async () => {
    authenticate('PROCUREMENT_OFFICER')

    await router.push('/')
    expect(router.currentRoute.value.name).toBe('quotations')

    await router.push('/suppliers')
    expect(router.currentRoute.value.name).toBe('suppliers')

    await router.push('/inventory')
    expect(router.currentRoute.value.name).toBe('inventory')

    await router.push('/email-records')
    expect(router.currentRoute.value.name).toBe('email-records')
  })

  it('blocks procurement officers from administration and catalog maintenance', async () => {
    authenticate('PROCUREMENT_OFFICER')

    await router.push('/users')
    expect(router.currentRoute.value.name).toBe('access-denied')

    await router.push('/products')
    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('routes warehouse officers to receiving and read-only purchasing pages', async () => {
    authenticate('WAREHOUSE_OFFICER')

    await router.push('/')
    expect(router.currentRoute.value.name).toBe('goods-receipts')

    await router.push('/purchase-orders')
    expect(router.currentRoute.value.name).toBe('purchase-orders')

    await router.push('/inventory')
    expect(router.currentRoute.value.name).toBe('inventory')
  })

  it('blocks warehouse officers from supplier quotations', async () => {
    authenticate('WAREHOUSE_OFFICER')

    await router.push('/quotations')

    expect(router.currentRoute.value.name).toBe('access-denied')

    await router.push('/email-records')
    expect(router.currentRoute.value.name).toBe('access-denied')
  })

  it('routes catalog managers to product maintenance', async () => {
    authenticate('CATALOG_MANAGER')

    await router.push('/')
    expect(router.currentRoute.value.name).toBe('products')

    await router.push('/product-categories')
    expect(router.currentRoute.value.name).toBe('product-categories')
  })

  it('blocks catalog managers from purchasing operations', async () => {
    authenticate('CATALOG_MANAGER')

    await router.push('/quotations')

    expect(router.currentRoute.value.name).toBe('access-denied')
  })
})
