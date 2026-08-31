import { createApp } from 'vue'

import App from './App.vue'
import router from './router'
import './assets/main.css'
import { setUnauthorizedHandler } from '@/services/apiClient'
import { useAuthStore } from '@/stores/auth'
import { pinia } from '@/stores'

const app = createApp(App)
const authStore = useAuthStore(pinia)

authStore.initialize()
setUnauthorizedHandler(async () => {
  authStore.logout()

  if (router.currentRoute.value.name !== 'login') {
    await router.replace({
      name: 'login',
      query: { reason: 'session-expired' },
    })
  }
})

app.use(pinia)
app.use(router)

app.mount('#app')
