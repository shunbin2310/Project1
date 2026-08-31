<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import type { WorkflowActorIdentity, WorkflowAvailableAction } from '@/types/purchaseRequest'

const props = defineProps<{
  action: WorkflowAvailableAction
  actor: WorkflowActorIdentity
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  execute: [comment: string | null]
}>()

const comment = ref('')
const commentError = ref('')
const isReject = computed(() => props.action.code === 'REJECT')

watch(
  () => props.action,
  () => {
    comment.value = ''
    commentError.value = ''
  },
  { immediate: true },
)

function submit() {
  commentError.value = ''
  const normalizedComment = comment.value.trim()

  if (props.action.requiresComment && !normalizedComment) {
    commentError.value = 'A comment is required for this action.'
    return
  }

  emit('execute', normalizedComment || null)
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('cancel')">
    <section
      class="modal-card workflow-action-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="workflow-action-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Workflow action</p>
          <h2 id="workflow-action-title">{{ action.name }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close action" @click="emit('cancel')">
          &times;
        </button>
      </header>

      <form class="workflow-action-form" novalidate @submit.prevent="submit">
        <div class="actor-summary">
          <span>Acting as</span>
          <strong>{{ actor.name }}</strong>
          <small>{{ actor.roles.length ? actor.roles.join(', ') : 'Authenticated user' }}</small>
        </div>

        <div v-if="errorMessage" class="form-server-error" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Action could not be completed</strong>
            <p>{{ errorMessage }}</p>
          </div>
        </div>

        <div class="workflow-transition">
          <span>{{ action.code }}</span>
          <strong>&rarr;</strong>
          <span>{{ action.toStepName }}</span>
        </div>

        <div class="form-field">
          <label for="workflow-comment">
            Comment {{ action.requiresComment ? '(required)' : '(optional)' }}
          </label>
          <textarea
            id="workflow-comment"
            v-model="comment"
            maxlength="500"
            rows="4"
            :placeholder="
              isReject ? 'Explain why this request is rejected' : 'Add an audit comment'
            "
            :aria-invalid="Boolean(commentError)"
          />
          <p v-if="commentError" class="field-error">{{ commentError }}</p>
        </div>

        <footer class="modal-actions">
          <button
            class="button button-secondary"
            type="button"
            :disabled="saving"
            @click="emit('cancel')"
          >
            Cancel
          </button>
          <button
            class="button"
            :class="isReject ? 'button-danger' : 'button-primary'"
            type="submit"
            :disabled="saving"
          >
            {{ saving ? 'Processing...' : action.name }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
