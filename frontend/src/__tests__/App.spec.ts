import { afterEach, describe, expect, it, vi } from 'vitest'

import { flushPromises, mount } from '@vue/test-utils'
import App from '../App.vue'

describe('App', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders the application title and API status', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({
          status: 'ok',
          message: 'Project1 API is running',
        }),
      }),
    )

    const wrapper = mount(App)

    expect(wrapper.get('h1').text()).toBe('Purchase & Inventory Management System')

    await flushPromises()

    expect(wrapper.text()).toContain('Project1 API is running')
  })
})
