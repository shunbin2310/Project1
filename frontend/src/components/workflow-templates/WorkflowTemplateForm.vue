<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'

import type { User } from '@/types/user'
import {
  workflowActionerRoles,
  workflowActionerTypes,
  type WorkflowActionDefinition,
  type WorkflowActionerDefinition,
  type WorkflowActionerType,
  type WorkflowStepDefinition,
  type WorkflowTemplate,
  type WorkflowTemplateFormValues,
} from '@/types/workflowTemplate'

const props = defineProps<{
  template: WorkflowTemplate | null
  users: User[]
  saving: boolean
  errorMessage: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: WorkflowTemplateFormValues]
}>()

const form = reactive<WorkflowTemplateFormValues>(emptyDefinition())
const validationError = ref('')
const isEditing = computed(() => props.template !== null)
const title = computed(() =>
  isEditing.value
    ? `Edit ${props.template?.code} version ${props.template?.version}`
    : 'Create workflow template',
)
const activeUsers = computed(() => props.users.filter((user) => user.isActive))

watch(
  () => props.template,
  (template) => {
    const definition = template ? fromTemplate(template) : emptyDefinition()
    form.code = definition.code
    form.name = definition.name
    form.entityType = definition.entityType
    form.steps = definition.steps
    validationError.value = ''
  },
  { immediate: true },
)

function emptyDefinition(): WorkflowTemplateFormValues {
  return {
    code: '',
    name: '',
    entityType: '',
    steps: [
      {
        code: 'DRAFT',
        name: 'Draft',
        displayOrder: 1,
        isInitial: true,
        isTerminal: false,
        actions: [
          {
            code: 'SUBMIT',
            name: 'Submit',
            toStepCode: 'APPROVED',
            requiresComment: false,
            actioners: [{ actionerType: 'Requester', actionerKey: null }],
          },
        ],
      },
      {
        code: 'APPROVED',
        name: 'Approved',
        displayOrder: 2,
        isInitial: false,
        isTerminal: true,
        actions: [],
      },
    ],
  }
}

function fromTemplate(template: WorkflowTemplate): WorkflowTemplateFormValues {
  return {
    code: template.code,
    name: template.name,
    entityType: template.entityType,
    steps: [...template.steps]
      .sort((left, right) => left.displayOrder - right.displayOrder)
      .map((step) => ({
        code: step.code,
        name: step.name,
        displayOrder: step.displayOrder,
        isInitial: step.isInitial,
        isTerminal: step.isTerminal,
        actions: step.actions.map((action) => ({
          code: action.code,
          name: action.name,
          toStepCode: action.toStepCode,
          requiresComment: action.requiresComment,
          actioners: action.actioners.map((actioner) => ({
            actionerType: actioner.actionerType,
            actionerKey: actioner.actionerKey,
          })),
        })),
      })),
  }
}

function addStep() {
  form.steps.push({
    code: `STEP_${form.steps.length + 1}`,
    name: `Step ${form.steps.length + 1}`,
    displayOrder: form.steps.length + 1,
    isInitial: false,
    isTerminal: false,
    actions: [],
  })
}

function removeStep(index: number) {
  if (form.steps.length <= 2) return
  form.steps.splice(index, 1)
  updateDisplayOrders()
}

function moveStep(index: number, offset: number) {
  const destination = index + offset
  if (destination < 0 || destination >= form.steps.length) return
  const [step] = form.steps.splice(index, 1)
  if (!step) return
  form.steps.splice(destination, 0, step)
  updateDisplayOrders()
}

function updateDisplayOrders() {
  form.steps.forEach((step, index) => {
    step.displayOrder = index + 1
  })
}

function setInitial(index: number) {
  form.steps.forEach((step, stepIndex) => {
    step.isInitial = stepIndex === index
  })
}

function toggleTerminal(step: WorkflowStepDefinition, event: Event) {
  step.isTerminal = (event.target as HTMLInputElement).checked
  if (step.isTerminal) step.actions = []
}

function addAction(step: WorkflowStepDefinition) {
  const target = form.steps.find((candidate) => candidate.code !== step.code)
  step.actions.push({
    code: `ACTION_${step.actions.length + 1}`,
    name: `Action ${step.actions.length + 1}`,
    toStepCode: target?.code ?? '',
    requiresComment: false,
    actioners: [{ actionerType: 'Role', actionerKey: 'REQUESTER' }],
  })
}

function removeAction(step: WorkflowStepDefinition, index: number) {
  step.actions.splice(index, 1)
}

function addActioner(action: WorkflowActionDefinition) {
  action.actioners.push({ actionerType: 'Role', actionerKey: 'REQUESTER' })
}

function removeActioner(action: WorkflowActionDefinition, index: number) {
  if (action.actioners.length <= 1) return
  action.actioners.splice(index, 1)
}

function changeActionerType(actioner: WorkflowActionerDefinition, event: Event) {
  const type = (event.target as HTMLSelectElement).value as WorkflowActionerType
  actioner.actionerType = type
  actioner.actionerKey =
    type === 'Requester'
      ? null
      : type === 'Role'
        ? 'REQUESTER'
        : (activeUsers.value[0]?.id.toString() ?? '')
}

function normalizeCode(value: string) {
  return value
    .trim()
    .toUpperCase()
    .replace(/[\s-]+/g, '_')
}

function validate() {
  validationError.value = ''
  const codePattern = /^[A-Z][A-Z0-9_]*$/
  const entityPattern = /^[A-Za-z][A-Za-z0-9._]*$/
  const code = normalizeCode(form.code)

  if (!codePattern.test(code) || code.length < 2 || code.length > 50) {
    validationError.value =
      'Template code must start with a letter and use only letters, numbers, or underscores.'
    return false
  }
  if (form.name.trim().length < 2 || form.name.trim().length > 150) {
    validationError.value = 'Template name must contain between 2 and 150 characters.'
    return false
  }
  if (!entityPattern.test(form.entityType.trim())) {
    validationError.value =
      'Entity type must start with a letter and use only letters, numbers, dots, or underscores.'
    return false
  }
  if (form.steps.length < 2) {
    validationError.value = 'Add at least two workflow steps.'
    return false
  }
  if (form.steps.filter((step) => step.isInitial).length !== 1) {
    validationError.value = 'Select exactly one initial step.'
    return false
  }
  if (!form.steps.some((step) => step.isTerminal)) {
    validationError.value = 'Select at least one terminal step.'
    return false
  }

  const stepCodes = new Set<string>()
  for (const step of form.steps) {
    const stepCode = normalizeCode(step.code)
    if (!codePattern.test(stepCode) || step.name.trim().length < 2) {
      validationError.value = 'Every step needs a valid code and a name.'
      return false
    }
    if (stepCodes.has(stepCode)) {
      validationError.value = `Step code ${stepCode} is duplicated.`
      return false
    }
    stepCodes.add(stepCode)
    if (!step.isTerminal && step.actions.length === 0) {
      validationError.value = `Non-terminal step ${stepCode} needs at least one action.`
      return false
    }

    for (const action of step.actions) {
      if (
        !codePattern.test(normalizeCode(action.code)) ||
        action.name.trim().length < 2 ||
        !action.toStepCode ||
        action.actioners.length === 0
      ) {
        validationError.value = `Complete every action in step ${stepCode}.`
        return false
      }
      if (
        action.actioners.some(
          (actioner) => actioner.actionerType !== 'Requester' && !actioner.actionerKey,
        )
      ) {
        validationError.value = `Select an actioner for every action in step ${stepCode}.`
        return false
      }
    }
  }

  return true
}

function submit() {
  if (!validate()) return

  emit('save', {
    code: normalizeCode(form.code),
    name: form.name.trim(),
    entityType: form.entityType.trim(),
    steps: form.steps.map((step, index) => ({
      code: normalizeCode(step.code),
      name: step.name.trim(),
      displayOrder: index + 1,
      isInitial: step.isInitial,
      isTerminal: step.isTerminal,
      actions: step.actions.map((action) => ({
        code: normalizeCode(action.code),
        name: action.name.trim(),
        toStepCode: normalizeCode(action.toStepCode),
        requiresComment: action.requiresComment,
        actioners: action.actioners.map((actioner) => ({
          actionerType: actioner.actionerType,
          actionerKey:
            actioner.actionerType === 'Requester' ? null : actioner.actionerKey?.trim() || null,
        })),
      })),
    })),
  })
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('cancel')">
    <section
      class="modal-card workflow-template-form-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="workflow-template-form-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Workflow definition</p>
          <h2 id="workflow-template-form-title">{{ title }}</h2>
        </div>
        <button class="icon-button" type="button" aria-label="Close form" @click="emit('cancel')">
          ×
        </button>
      </header>

      <form class="workflow-template-form" novalidate @submit.prevent="submit">
        <div v-if="errorMessage || validationError" class="form-server-error" role="alert">
          <span aria-hidden="true">!</span>
          <div>
            <strong>Workflow template could not be saved</strong>
            <p>{{ errorMessage || validationError }}</p>
          </div>
        </div>

        <section class="workflow-definition-section">
          <div class="workflow-definition-grid">
            <div class="form-field">
              <label for="workflow-template-code">Template code</label>
              <input
                id="workflow-template-code"
                v-model="form.code"
                maxlength="50"
                placeholder="e.g. PURCHASE_REQUEST"
                :readonly="isEditing"
              />
              <p v-if="isEditing" class="field-hint">Code cannot change after creation.</p>
            </div>
            <div class="form-field">
              <label for="workflow-template-entity">Entity type</label>
              <input
                id="workflow-template-entity"
                v-model="form.entityType"
                maxlength="100"
                placeholder="e.g. PurchaseRequest"
                :readonly="isEditing"
              />
              <p v-if="isEditing" class="field-hint">Entity type is shared by every version.</p>
            </div>
            <div class="form-field workflow-definition-name">
              <label for="workflow-template-name">Template name</label>
              <input
                id="workflow-template-name"
                v-model="form.name"
                maxlength="150"
                placeholder="e.g. Purchase Request Approval"
              />
            </div>
          </div>
        </section>

        <section class="workflow-steps-editor" aria-labelledby="workflow-steps-title">
          <div class="workflow-editor-heading">
            <div>
              <h3 id="workflow-steps-title">Workflow steps</h3>
              <p>Arrange the stages, then define the action that moves each stage forward.</p>
            </div>
            <button class="button button-secondary" type="button" @click="addStep">
              + Add step
            </button>
          </div>

          <article
            v-for="(step, stepIndex) in form.steps"
            :key="stepIndex"
            class="workflow-step-editor"
          >
            <header>
              <div class="workflow-step-number">{{ stepIndex + 1 }}</div>
              <strong>{{ step.name || 'Unnamed step' }}</strong>
              <div class="workflow-editor-controls">
                <button
                  type="button"
                  aria-label="Move step up"
                  :disabled="stepIndex === 0"
                  @click="moveStep(stepIndex, -1)"
                >
                  ↑
                </button>
                <button
                  type="button"
                  aria-label="Move step down"
                  :disabled="stepIndex === form.steps.length - 1"
                  @click="moveStep(stepIndex, 1)"
                >
                  ↓
                </button>
                <button
                  class="is-danger"
                  type="button"
                  aria-label="Remove step"
                  :disabled="form.steps.length <= 2"
                  @click="removeStep(stepIndex)"
                >
                  Remove
                </button>
              </div>
            </header>

            <div class="workflow-step-fields">
              <div class="form-field">
                <label :for="`workflow-step-code-${stepIndex}`">Step code</label>
                <input :id="`workflow-step-code-${stepIndex}`" v-model="step.code" maxlength="50" />
              </div>
              <div class="form-field">
                <label :for="`workflow-step-name-${stepIndex}`">Step name</label>
                <input
                  :id="`workflow-step-name-${stepIndex}`"
                  v-model="step.name"
                  maxlength="100"
                />
              </div>
              <label class="workflow-checkbox">
                <input
                  type="radio"
                  name="initial-step"
                  :checked="step.isInitial"
                  @change="setInitial(stepIndex)"
                />
                Initial step
              </label>
              <label class="workflow-checkbox">
                <input
                  type="checkbox"
                  :checked="step.isTerminal"
                  @change="toggleTerminal(step, $event)"
                />
                Terminal step
              </label>
            </div>

            <div v-if="!step.isTerminal" class="workflow-actions-editor">
              <div class="workflow-subheading">
                <strong>Actions</strong>
                <button class="text-button" type="button" @click="addAction(step)">
                  + Add action
                </button>
              </div>

              <div v-if="step.actions.length === 0" class="workflow-inline-empty">
                Add an action so this step can move to another step.
              </div>

              <article
                v-for="(action, actionIndex) in step.actions"
                :key="actionIndex"
                class="workflow-action-editor"
              >
                <div class="workflow-action-fields">
                  <div class="form-field">
                    <label :for="`workflow-action-code-${stepIndex}-${actionIndex}`">Code</label>
                    <input
                      :id="`workflow-action-code-${stepIndex}-${actionIndex}`"
                      v-model="action.code"
                      maxlength="50"
                    />
                  </div>
                  <div class="form-field">
                    <label :for="`workflow-action-name-${stepIndex}-${actionIndex}`">Name</label>
                    <input
                      :id="`workflow-action-name-${stepIndex}-${actionIndex}`"
                      v-model="action.name"
                      maxlength="100"
                    />
                  </div>
                  <div class="form-field">
                    <label :for="`workflow-action-target-${stepIndex}-${actionIndex}`"
                      >Moves to</label
                    >
                    <select
                      :id="`workflow-action-target-${stepIndex}-${actionIndex}`"
                      v-model="action.toStepCode"
                    >
                      <option value="" disabled>Select destination</option>
                      <option
                        v-for="target in form.steps.filter((candidate) => candidate !== step)"
                        :key="target.code"
                        :value="target.code"
                      >
                        {{ target.name }} ({{ target.code }})
                      </option>
                    </select>
                  </div>
                  <label class="workflow-checkbox">
                    <input v-model="action.requiresComment" type="checkbox" />
                    Comment required
                  </label>
                </div>

                <div class="workflow-actioners">
                  <div class="workflow-subheading">
                    <strong>Allowed actioners</strong>
                    <button class="text-button" type="button" @click="addActioner(action)">
                      + Add actioner
                    </button>
                  </div>
                  <div
                    v-for="(actioner, actionerIndex) in action.actioners"
                    :key="actionerIndex"
                    class="workflow-actioner-row"
                  >
                    <select
                      :aria-label="`Actioner type ${actionerIndex + 1}`"
                      :value="actioner.actionerType"
                      @change="changeActionerType(actioner, $event)"
                    >
                      <option v-for="type in workflowActionerTypes" :key="type" :value="type">
                        {{ type }}
                      </option>
                    </select>
                    <span v-if="actioner.actionerType === 'Requester'" class="actioner-description">
                      The user who created the business record
                    </span>
                    <select
                      v-else-if="actioner.actionerType === 'Role'"
                      v-model="actioner.actionerKey"
                      :aria-label="`Actioner role ${actionerIndex + 1}`"
                    >
                      <option v-for="role in workflowActionerRoles" :key="role" :value="role">
                        {{ role }}
                      </option>
                    </select>
                    <select
                      v-else
                      v-model="actioner.actionerKey"
                      :aria-label="`Actioner user ${actionerIndex + 1}`"
                    >
                      <option value="" disabled>Select an active user</option>
                      <option
                        v-for="user in activeUsers"
                        :key="user.id"
                        :value="user.id.toString()"
                      >
                        {{ user.fullName }} ({{ user.email }})
                      </option>
                    </select>
                    <button
                      class="text-button text-button-danger"
                      type="button"
                      :disabled="action.actioners.length <= 1"
                      @click="removeActioner(action, actionerIndex)"
                    >
                      Remove
                    </button>
                  </div>
                </div>

                <button
                  class="text-button text-button-danger workflow-remove-action"
                  type="button"
                  @click="removeAction(step, actionIndex)"
                >
                  Remove action
                </button>
              </article>
            </div>
            <p v-else class="workflow-terminal-note">
              Terminal steps finish the workflow and have no actions.
            </p>
          </article>
        </section>

        <footer class="modal-actions">
          <button
            class="button button-secondary"
            type="button"
            :disabled="saving"
            @click="emit('cancel')"
          >
            Cancel
          </button>
          <button class="button button-primary" type="submit" :disabled="saving">
            {{ saving ? 'Saving…' : isEditing ? 'Save draft' : 'Create draft' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
