import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import { authService } from '@/services/authService'
import {
  clearAuthSession,
  isSessionExpired,
  readAuthSession,
  writeAuthSession,
} from '@/services/authSession'
import type { ApplicationRole, AuthSession, LoginRequest } from '@/types/auth'

export const useAuthStore = defineStore('auth', () => {
  const session = ref<AuthSession | null>(null)
  const loggingIn = ref(false)

  const user = computed(() => session.value?.user ?? null)
  const roles = computed(() => user.value?.roles ?? [])
  const isAuthenticated = computed(() => session.value !== null && !isSessionExpired(session.value))

  function initialize() {
    const storedSession = readAuthSession()

    if (storedSession && isSessionExpired(storedSession)) {
      clearAuthSession()
      session.value = null
      return
    }

    session.value = storedSession
  }

  async function login(request: LoginRequest) {
    loggingIn.value = true

    try {
      const response = await authService.login(request)
      session.value = response
      writeAuthSession(response)
      return response.user
    } finally {
      loggingIn.value = false
    }
  }

  function logout() {
    clearAuthSession()
    session.value = null
  }

  function hasAnyRole(requiredRoles: readonly ApplicationRole[]) {
    return requiredRoles.length === 0 || requiredRoles.some((role) => roles.value.includes(role))
  }

  return {
    session,
    loggingIn,
    user,
    roles,
    isAuthenticated,
    initialize,
    login,
    logout,
    hasAnyRole,
  }
})
