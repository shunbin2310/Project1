import { describe, expect, it } from 'vitest'
import { computed, effectScope, nextTick, ref } from 'vue'

import { usePagination } from '../usePagination'

describe('usePagination', () => {
  it('resets on a filter change even when the result count stays the same', async () => {
    const scope = effectScope()
    const filter = ref('alpha')
    const items = computed(() =>
      Array.from({ length: 21 }, (_, index) => `${filter.value}-${index}`),
    )
    const pagination = scope.run(() => usePagination(items, [filter]))!

    pagination.page.value = 2
    expect(pagination.paginatedItems.value[0]).toBe('alpha-10')
    filter.value = 'beta'
    await nextTick()

    expect(pagination.page.value).toBe(1)
    expect(pagination.paginatedItems.value[0]).toBe('beta-0')
    scope.stop()
  })

  it('clamps the page when records disappear and keeps a valid page on reload', async () => {
    const scope = effectScope()
    const items = ref(Array.from({ length: 21 }, (_, index) => index))
    const pagination = scope.run(() => usePagination(items))!

    pagination.page.value = 3
    items.value = items.value.slice(0, 20)
    await nextTick()
    expect(pagination.page.value).toBe(2)
    expect(pagination.paginatedItems.value).toHaveLength(10)

    items.value = [...items.value]
    await nextTick()
    expect(pagination.page.value).toBe(2)

    items.value = []
    await nextTick()
    expect(pagination.page.value).toBe(1)
    expect(pagination.paginatedItems.value).toEqual([])
    scope.stop()
  })
})
