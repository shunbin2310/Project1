<script setup lang="ts">
import { onMounted, ref } from 'vue'

const apiMessage = ref('Connecting to API...')
const errorMessage = ref('')

onMounted(async () => {
  try {
    const response = await fetch('http://localhost:5165/api/health')

    if (!response.ok) {
      throw new Error(`HTTP error: ${response.status}`)
    }

    const data = (await response.json()) as {
      status: string
      message: string
    }

    apiMessage.value = data.message
  } catch (error) {
    apiMessage.value = ''
    errorMessage.value = error instanceof Error ? error.message : 'Unable to connect to API'
  }
})
</script>

<template>
  <main>
    <h1>Purchase & Inventory Management System</h1>

    <p v-if="apiMessage">{{ apiMessage }}</p>
    <p v-if="errorMessage">API error: {{ errorMessage }}</p>
  </main>
</template>