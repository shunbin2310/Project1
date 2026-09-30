<script setup lang="ts">
import type { EmailTemplatePreview } from '@/types/emailTemplate'

defineProps<{
  preview: EmailTemplatePreview
  version: number
}>()

const emit = defineEmits<{
  close: []
}>()
</script>

<template>
  <div class="modal-backdrop email-template-preview-backdrop" @click.self="emit('close')">
    <section
      class="modal-card email-template-preview-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="email-template-preview-title"
    >
      <header class="modal-header">
        <div>
          <p class="eyebrow">Sample email · Version {{ version }}</p>
          <h2 id="email-template-preview-title">Email Preview</h2>
          <p>Rendered with fixed example Purchase Order data.</p>
        </div>
        <button class="icon-button" type="button" aria-label="Close preview" @click="emit('close')">
          &times;
        </button>
      </header>

      <div class="email-template-preview-content">
        <section class="email-template-preview-addresses">
          <div><span>To</span><strong>{{ preview.recipientEmail }}</strong></div>
          <div><span>CC</span><strong>{{ preview.ccRecipients || 'None' }}</strong></div>
          <div><span>BCC</span><strong>{{ preview.bccRecipients || 'None' }}</strong></div>
        </section>

        <section class="email-template-preview-subject">
          <span>Subject</span>
          <strong>{{ preview.subject }}</strong>
        </section>

        <section class="email-record-content">
          <div>
            <strong>Rendered HTML</strong>
            <span>Safe preview using sample values</span>
          </div>
          <iframe title="Email template preview" sandbox="" :srcdoc="preview.htmlBody"></iframe>
        </section>

        <footer class="modal-actions">
          <button class="button button-secondary" type="button" @click="emit('close')">Close</button>
        </footer>
      </div>
    </section>
  </div>
</template>
