<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    page: number
    pageSize: number
    total: number
    label: string
    itemLabel?: string
  }>(),
  { itemLabel: 'records' },
)

const emit = defineEmits<{
  'update:page': [value: number]
  'update:pageSize': [value: number]
}>()

const pageCount = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))
const firstItem = computed(() => (props.total === 0 ? 0 : (props.page - 1) * props.pageSize + 1))
const lastItem = computed(() => Math.min(props.page * props.pageSize, props.total))

function changePageSize(event: Event) {
  emit('update:pageSize', Number((event.target as HTMLSelectElement).value))
}
</script>

<template>
  <nav class="table-pagination" :aria-label="`${label} pagination`">
    <p class="pagination-summary" aria-live="polite">
      Showing {{ firstItem }}–{{ lastItem }} of {{ total }} {{ itemLabel }}
    </p>
    <label class="pagination-size">
      <span>Rows per page</span>
      <select :aria-label="`${label} rows per page`" :value="pageSize" @change="changePageSize">
        <option :value="10">10</option>
        <option :value="20">20</option>
        <option :value="50">50</option>
      </select>
    </label>
    <div class="pagination-controls">
      <button
        class="button button-secondary"
        type="button"
        :aria-label="`Previous ${label.toLowerCase()} page`"
        :disabled="page <= 1"
        @click="emit('update:page', page - 1)"
      >
        Previous
      </button>
      <span>Page {{ page }} of {{ pageCount }}</span>
      <button
        class="button button-secondary"
        type="button"
        :aria-label="`Next ${label.toLowerCase()} page`"
        :disabled="page >= pageCount"
        @click="emit('update:page', page + 1)"
      >
        Next
      </button>
    </div>
  </nav>
</template>

<style scoped>
.table-pagination {
  display: flex;
  flex-wrap: wrap;
  gap: 16px 24px;
  align-items: center;
  justify-content: space-between;
  padding: 16px 22px;
  color: var(--muted);
  font-size: var(--font-size-caption);
  border-top: 1px solid var(--line);
}

.pagination-summary {
  margin: 0;
}

.pagination-size,
.pagination-controls {
  display: flex;
  gap: 10px;
  align-items: center;
}

.pagination-size select {
  min-height: 34px;
  padding: 4px 8px;
  color: inherit;
  font: inherit;
  font-size: var(--font-size-control);
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: 6px;
}

.pagination-controls .button {
  min-height: 34px;
  padding: 6px 12px;
}
</style>
