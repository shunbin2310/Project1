import { afterEach, describe, expect, it, vi } from 'vitest'

import { mount } from '@vue/test-utils'
import { defineComponent, nextTick } from 'vue'
import { useToast } from '../useToast'

const TestHost = defineComponent({
  setup() {
    return useToast()
  },
  template: `
    <button type="button" @click="showSuccess('Record saved.')">Show</button>
    <p v-if="toast">{{ toast.message }}</p>
  `,
})

afterEach(() => {
  vi.useRealTimers()
})

describe('useToast', () => {
  it('shows a success toast and automatically dismisses it', async () => {
    vi.useFakeTimers()
    const wrapper = mount(TestHost)

    await wrapper.get('button').trigger('click')
    expect(wrapper.text()).toContain('Record saved.')

    vi.advanceTimersByTime(3500)
    await nextTick()

    expect(wrapper.find('p').exists()).toBe(false)
  })
})
