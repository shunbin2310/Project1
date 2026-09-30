<script setup lang="ts">
import { reactive, ref } from 'vue'

import type {
  EmailTemplate,
  PreviewEmailTemplateRequest,
  UpdateEmailTemplateRequest,
} from '@/types/emailTemplate'

const props = defineProps<{
  template: EmailTemplate
  saving?: boolean
  previewing?: boolean
  errorMessage?: string
}>()

const emit = defineEmits<{
  cancel: []
  save: [values: UpdateEmailTemplateRequest]
  preview: [values: PreviewEmailTemplateRequest]
}>()

const form = reactive<UpdateEmailTemplateRequest>({
  name: props.template.name,
  subjectTemplate: props.template.subjectTemplate,
  htmlBodyTemplate: props.template.htmlBodyTemplate,
  ccRecipients: props.template.ccRecipients,
  bccRecipients: props.template.bccRecipients,
})
const clientError = ref('')
const focusedField = ref<'subject' | 'body'>('body')

function values(): UpdateEmailTemplateRequest {
  return {
    name: form.name.trim(),
    subjectTemplate: form.subjectTemplate.trim(),
    htmlBodyTemplate: form.htmlBodyTemplate.trim(),
    ccRecipients: form.ccRecipients?.trim() || null,
    bccRecipients: form.bccRecipients?.trim() || null,
  }
}

function validate() {
  clientError.value = ''
  const payload = values()
  if (payload.name.length < 2) clientError.value = 'Template name is required.'
  else if (!payload.subjectTemplate) clientError.value = 'Email subject is required.'
  else if (!payload.htmlBodyTemplate) clientError.value = 'HTML body is required.'
  return clientError.value === ''
}

function submit() {
  if (validate()) emit('save', values())
}

function preview() {
  if (!validate()) return
  const payload = values()
  emit('preview', {
    subjectTemplate: payload.subjectTemplate,
    htmlBodyTemplate: payload.htmlBodyTemplate,
    ccRecipients: payload.ccRecipients,
    bccRecipients: payload.bccRecipients,
  })
}

function insertPlaceholder(placeholder: string) {
  const token = `{{${placeholder}}}`
  if (focusedField.value === 'subject' && placeholder !== 'ItemsTable') {
    form.subjectTemplate = `${form.subjectTemplate}${token}`
    return
  }
  form.htmlBodyTemplate = `${form.htmlBodyTemplate}${token}`
}

function formatPlaceholder(placeholder: string) {
  return `{{${placeholder}}}`
}
</script>

<template>
  <div class="modal-backdrop" @click.self="!saving && emit('cancel')">
    <section
      class="modal-card email-template-editor-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="email-template-editor-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Email template draft</p>
          <h2 id="email-template-editor-title">{{ template.code }} · Version {{ template.version }}</h2>
          <p>Supplier Email is resolved automatically when a Purchase Order is issued.</p>
        </div>
        <button
          class="icon-button"
          type="button"
          aria-label="Close email template editor"
          :disabled="saving"
          @click="emit('cancel')"
        >
          &times;
        </button>
      </header>

      <form class="email-template-editor" novalidate @submit.prevent="submit">
        <div v-if="clientError || errorMessage" class="alert alert-error" role="alert">
          {{ clientError || errorMessage }}
        </div>

        <section class="email-template-definition-grid">
          <label class="form-field">
            <span>Template name</span>
            <input v-model="form.name" type="text" maxlength="150" />
          </label>
          <label class="form-field">
            <span>To rule</span>
            <input value="Supplier Email (automatic)" type="text" readonly />
          </label>
          <label class="form-field">
            <span>Default CC</span>
            <input v-model="form.ccRecipients" type="text" placeholder="finance@company.com" />
            <small>Separate multiple addresses with commas.</small>
          </label>
          <label class="form-field">
            <span>Default BCC</span>
            <input v-model="form.bccRecipients" type="text" placeholder="audit@company.com" />
            <small>Optional hidden recipients.</small>
          </label>
        </section>

        <label class="form-field">
          <span>Email subject</span>
          <input
            v-model="form.subjectTemplate"
            type="text"
            maxlength="300"
            @focus="focusedField = 'subject'"
          />
        </label>

        <section class="email-template-placeholder-panel">
          <div>
            <strong>Available placeholders</strong>
            <span>Click to insert into the last selected Subject or HTML Body field.</span>
          </div>
          <div class="email-template-placeholder-list">
            <button
              v-for="placeholder in template.supportedPlaceholders"
              :key="placeholder"
              type="button"
              :disabled="focusedField === 'subject' && placeholder === 'ItemsTable'"
              @click="insertPlaceholder(placeholder)"
            >
              {{ formatPlaceholder(placeholder) }}
            </button>
          </div>
        </section>

        <label class="form-field email-template-html-field">
          <span>HTML body</span>
          <textarea
            v-model="form.htmlBodyTemplate"
            rows="20"
            maxlength="50000"
            spellcheck="false"
            @focus="focusedField = 'body'"
          ></textarea>
          <small>{{ form.htmlBodyTemplate.length }}/50000 characters</small>
        </label>

        <footer class="modal-actions">
          <button class="button button-secondary" type="button" :disabled="saving" @click="emit('cancel')">
            Cancel
          </button>
          <button
            class="button button-secondary"
            type="button"
            :disabled="saving || previewing"
            @click="preview"
          >
            {{ previewing ? 'Rendering...' : 'Preview' }}
          </button>
          <button class="button button-primary" type="submit" :disabled="saving || previewing">
            {{ saving ? 'Saving...' : 'Save draft' }}
          </button>
        </footer>
      </form>
    </section>
  </div>
</template>
