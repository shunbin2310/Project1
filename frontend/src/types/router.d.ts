import 'vue-router'

import type { ApplicationRole } from '@/types/auth'

declare module 'vue-router' {
  interface RouteMeta {
    title?: string
    requiresAuth?: boolean
    guestOnly?: boolean
    layout?: 'auth'
    roles?: readonly ApplicationRole[]
  }
}
