import { onScopeDispose, ref } from 'vue'

export type ToastVariant = 'success' | 'error'

export interface ToastMessage {
  title: string
  message: string
  variant: ToastVariant
}

interface ToastOptions {
  title?: string
  variant?: ToastVariant
  duration?: number
}

export function useToast() {
  const toast = ref<ToastMessage | null>(null)
  let timer: number | undefined

  function showToast(message: string, options: ToastOptions = {}) {
    window.clearTimeout(timer)
    toast.value = {
      title: options.title ?? 'Action completed',
      message,
      variant: options.variant ?? 'success',
    }

    timer = window.setTimeout(dismissToast, options.duration ?? 3500)
  }

  function showSuccess(message: string, title = 'Action completed') {
    showToast(message, { title, variant: 'success' })
  }

  function showError(message: string, title = 'Action failed') {
    showToast(message, { title, variant: 'error', duration: 5000 })
  }

  function dismissToast() {
    window.clearTimeout(timer)
    timer = undefined
    toast.value = null
  }

  onScopeDispose(dismissToast)

  return { toast, showToast, showSuccess, showError, dismissToast }
}
