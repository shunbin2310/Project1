import type { AuthSession } from '@/types/auth'

export const authSessionStorageKey = 'project1.auth.session'

export function readAuthSession(): AuthSession | null {
  const value = window.sessionStorage.getItem(authSessionStorageKey)
  if (!value) return null

  try {
    const session = JSON.parse(value) as Partial<AuthSession>
    if (
      typeof session.accessToken !== 'string' ||
      typeof session.expiresAtUtc !== 'string' ||
      !session.user ||
      typeof session.user.id !== 'number'
    ) {
      clearAuthSession()
      return null
    }

    return session as AuthSession
  } catch {
    clearAuthSession()
    return null
  }
}

export function writeAuthSession(session: AuthSession) {
  window.sessionStorage.setItem(authSessionStorageKey, JSON.stringify(session))
}

export function clearAuthSession() {
  window.sessionStorage.removeItem(authSessionStorageKey)
}

export function getAccessToken() {
  return readAuthSession()?.accessToken ?? null
}

export function isSessionExpired(session: AuthSession) {
  const expiresAt = Date.parse(session.expiresAtUtc)
  return !Number.isFinite(expiresAt) || expiresAt <= Date.now()
}
