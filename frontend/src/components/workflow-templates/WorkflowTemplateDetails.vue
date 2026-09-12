<script setup lang="ts">
import { computed } from 'vue'

import type { User } from '@/types/user'
import type { WorkflowTemplate, WorkflowTemplateActioner } from '@/types/workflowTemplate'
import { orderWorkflowSteps } from '@/utils/workflowTemplateOrdering'

const props = defineProps<{
  template: WorkflowTemplate
  users: User[]
}>()

const emit = defineEmits<{
  close: []
}>()

const orderedSteps = computed(() => orderWorkflowSteps(props.template.steps))

function statusLabel() {
  if (!props.template.isPublished) return 'Draft'
  return props.template.isActive ? 'Active' : 'Superseded'
}

function actionerLabel(actioner: WorkflowTemplateActioner) {
  if (actioner.actionerType === 'Requester') return 'Record requester'
  if (actioner.actionerType === 'Role') return `Role: ${actioner.actionerKey}`

  const user = props.users.find((record) => record.id.toString() === actioner.actionerKey)
  return user ? `User: ${user.fullName}` : `User ID: ${actioner.actionerKey}`
}

function formatDate(value: string | null) {
  if (!value) return 'Not published'
  return new Intl.DateTimeFormat('en-MY', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('close')">
    <section
      class="modal-card workflow-template-details-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="workflow-template-details-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Workflow template</p>
          <h2 id="workflow-template-details-title">
            {{ template.code }}
            <span class="workflow-version-heading">Version {{ template.version }}</span>
          </h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close details" @click="emit('close')">
          ×
        </button>
      </header>

      <div class="workflow-template-details">
        <div class="workflow-details-summary">
          <article>
            <span>Name</span>
            <strong>{{ template.name }}</strong>
          </article>
          <article>
            <span>Entity type</span>
            <strong>{{ template.entityType }}</strong>
          </article>
          <article>
            <span>Status</span>
            <strong>{{ statusLabel() }}</strong>
          </article>
          <article>
            <span>Published</span>
            <strong>{{ formatDate(template.publishedAtUtc) }}</strong>
          </article>
        </div>

        <section class="workflow-flow" aria-labelledby="workflow-flow-title">
          <div class="workflow-editor-heading">
            <div>
              <h3 id="workflow-flow-title">Steps and transitions</h3>
              <p>This definition is copied into each new workflow instance.</p>
            </div>
          </div>

          <article
            v-for="(step, stepIndex) in orderedSteps"
            :key="step.id"
            class="workflow-step-card"
          >
            <div class="workflow-step-rail">
              <span>{{ stepIndex + 1 }}</span>
            </div>
            <div class="workflow-step-content">
              <header>
                <div>
                  <strong>{{ step.name }}</strong>
                  <small>{{ step.code }}</small>
                </div>
                <div class="workflow-step-tags">
                  <span v-if="step.isInitial">Initial</span>
                  <span v-if="step.isTerminal" class="is-terminal">Terminal</span>
                </div>
              </header>

              <p v-if="step.actions.length === 0" class="workflow-terminal-note">
                Workflow ends at this step.
              </p>
              <div v-else class="workflow-action-list">
                <article v-for="action in step.actions" :key="action.id">
                  <div class="workflow-action-route">
                    <span>
                      <strong>{{ action.name }}</strong>
                      <small>{{ action.code }}</small>
                    </span>
                    <span aria-hidden="true">→</span>
                    <strong>{{ action.toStepCode }}</strong>
                  </div>
                  <div class="workflow-action-meta">
                    <span v-if="action.requiresComment">Comment required</span>
                    <span v-for="actioner in action.actioners" :key="actioner.id">
                      {{ actionerLabel(actioner) }}
                    </span>
                  </div>
                </article>
              </div>
            </div>
          </article>
        </section>
      </div>
    </section>
  </div>
</template>
