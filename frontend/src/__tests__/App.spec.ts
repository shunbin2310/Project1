import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { mount } from '@vue/test-utils'
import App from '../App.vue'

describe('App', () => {
  it('renders the workspace navigation and current page', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: '/departments',
          component: { template: '<h1>Departments</h1>' },
          meta: { title: 'Departments' },
        },
      ],
    })

    await router.push('/departments')
    await router.isReady()

    const wrapper = mount(App, {
      global: { plugins: [router] },
    })

    expect(wrapper.text()).toContain('Purchase & Inventory')
    expect(wrapper.get('h1').text()).toBe('Departments')
    expect(wrapper.get('a.router-link-active').text()).toContain('Departments')
    expect(wrapper.get('a[href="/suppliers"]').text()).toContain('Suppliers')
  })
})
