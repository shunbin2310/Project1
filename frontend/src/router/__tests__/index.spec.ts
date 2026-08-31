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

  it('allows every authenticated role to open My Tasks', async () => {
    authenticate('FINANCE_APPROVER')

    await router.push('/my-tasks')

    expect(router.currentRoute.value.name).toBe('my-tasks')
  })
})
