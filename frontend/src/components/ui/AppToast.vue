<script setup lang="ts">
import type { ToastMessage } from '@/composables/useToast'

defineProps<{
  toast: ToastMessage | null
}>()

const emit = defineEmits<{
  dismiss: []
}>()
</script>

<template>
  <Transition name="toast">
    <div
      v-if="toast"
      class="app-toast"
      :class="`${toast.variant}-toast`"
      :role="toast.variant === 'error' ? 'alert' : 'status'"
      :aria-live="toast.variant === 'error' ? 'assertive' : 'polite'"
    >
      <span class="app-toast-icon" aria-hidden="true">
        {{ toast.variant === 'success' ? 'OK' : '!' }}
      </span>
      <div>
        <strong>{{ toast.title }}</strong>
        <p>{{ toast.message }}</p>
      </div>
      <button type="button" aria-label="Dismiss notification" @click="emit('dismiss')">
        &times;
      </button>
    </div>
  </Transition>
</template>
