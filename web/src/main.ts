import { createApp } from 'vue'
import App from './App.vue'
import { router } from './router'
import { apiBase } from './lib/config'
import './styles/base.css'

// Resolve (and persist) ?api= before the router strips anything.
apiBase()

createApp(App).use(router).mount('#app')
