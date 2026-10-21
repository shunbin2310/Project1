import { computed, ref, watch, type Ref, type WatchSource } from 'vue'

export function usePagination<T>(
  items: Readonly<Ref<readonly T[]>>,
  resetSources: WatchSource[] = [],
) {
  const page = ref(1)
  const pageSize = ref(10)
  const pageCount = computed(() => Math.max(1, Math.ceil(items.value.length / pageSize.value)))
  const paginatedItems = computed(() => {
    const start = (page.value - 1) * pageSize.value
    return items.value.slice(start, start + pageSize.value)
  })

  watch([() => items.value.length, pageSize], ([, size], [, previousSize]) => {
    page.value = size !== previousSize ? 1 : Math.min(page.value, pageCount.value)
  })

  if (resetSources.length) {
    watch(resetSources, () => {
      page.value = 1
    })
  }

  return { page, pageSize, paginatedItems }
}
