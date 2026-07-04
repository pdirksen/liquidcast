import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import Aura from '@primevue/themes/aura'
import { definePreset } from '@primevue/themes'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import Tooltip from 'primevue/tooltip'

import 'primeicons/primeicons.css'
import './style.css'

import App from './App.vue'
import router from './router'
import { i18n, setLocale } from './i18n'

setLocale(i18n.global.locale.value)

// App is always dark. PrimeVue v4 emits its dark tokens under `:root.dark`, so the
// class must live on <html> (not a nested div) for components to pick up dark mode.
document.documentElement.classList.add('dark')

// Muted-blue accent + elevated-slate surfaces so PrimeVue components (tables,
// dialogs, inputs, buttons) match the custom chrome tokens in style.css.
const slate = {
  0: '#ffffff', 50: '#f5f7fa', 100: '#e9edf3', 200: '#cfd6e0', 300: '#aab3c2',
  400: '#7d8799', 500: '#5c6675', 600: '#454d5c', 700: '#353c49', 800: '#2a303b',
  900: '#242a35', 950: '#1b1f27',
}
const mutedBlue = {
  50: '#eef3fb', 100: '#d6e1f5', 200: '#b3c8ea', 300: '#8eaadd', 400: '#7295d3',
  500: '#5f86c9', 600: '#4f6fae', 700: '#42598c', 800: '#3a4b73', 900: '#33405f',
  950: '#222a3d',
}
const Liquidcast = definePreset(Aura, {
  primitive: { surface: slate },
  semantic: {
    primary: mutedBlue,
    // Dark mode reads its own surface palette (Aura defaults to near-black zinc) —
    // override it to the slate scale so tables/inputs/dialogs match the cards.
    colorScheme: { dark: { surface: slate } },
  },
})

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.use(i18n)
app.use(PrimeVue, {
  theme: { preset: Liquidcast, options: { darkModeSelector: '.dark' } },
})
app.use(ToastService)
app.use(ConfirmationService)
app.directive('tooltip', Tooltip)
app.mount('#app')
