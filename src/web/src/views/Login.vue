<script setup>
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuth } from '../stores/auth'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'

const auth = useAuth()
const router = useRouter()
const route = useRoute()
const { t } = useI18n()

const username = ref('admin')
const password = ref('')
const error = ref('')
const busy = ref(false)

async function submit() {
  error.value = ''
  busy.value = true
  try {
    await auth.login(username.value, password.value)
    router.push(route.query.redirect || '/monitor')
  } catch {
    error.value = t('login.invalid')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login-wrap">
    <div class="glow glow-a" />
    <div class="glow glow-b" />
    <div class="glow glow-c" />

    <form class="card" @submit.prevent="submit">
      <div class="brand">
        <svg class="mark" viewBox="0 0 120 120" role="img" aria-label="Liquidcast">
          <defs>
            <linearGradient id="lc-liquid" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0" stop-color="#38e0f0" />
              <stop offset="0.55" stop-color="#22a7f0" />
              <stop offset="1" stop-color="#3b5bf6" />
            </linearGradient>
          </defs>
          <path d="M60 12 C 74 34, 94 54, 94 74 A 34 34 0 1 1 26 74 C 26 54, 46 34, 60 12 Z" fill="url(#lc-liquid)" />
          <g fill="none" stroke="#EAF2F6" stroke-linecap="round">
            <circle cx="60" cy="84" r="5.5" fill="#EAF2F6" stroke="none" />
            <path d="M45 76 A 18 18 0 0 1 75 76" stroke-width="6" opacity="0.95" />
            <path d="M38 68 A 28 28 0 0 1 82 68" stroke-width="6" opacity="0.7" />
          </g>
        </svg>
        <h1>Liquidcast</h1>
        <p class="muted">{{ t('login.subtitle') }}</p>
      </div>

      <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>

      <label>{{ t('login.username') }}</label>
      <IconField>
        <InputIcon class="pi pi-user" />
        <InputText v-model="username" autofocus class="w-full" />
      </IconField>

      <label>{{ t('login.password') }}</label>
      <IconField>
        <InputIcon class="pi pi-lock" />
        <Password v-model="password" :feedback="false" toggleMask inputClass="w-full" class="w-full" />
      </IconField>

      <Button type="submit" :loading="busy" :label="t('login.signIn')" class="mt submit-btn" />
    </form>
  </div>
</template>

<style scoped>
.login-wrap {
  position: relative;
  display: grid;
  place-items: center;
  min-height: 100vh;
  overflow: hidden;
  background: radial-gradient(ellipse at 50% -10%, #232a38 0%, var(--bg) 55%);
}

/* Slow-drifting brand-colored blobs behind the card, purely decorative. */
.glow {
  position: absolute;
  border-radius: 50%;
  filter: blur(70px);
  opacity: 0.35;
  pointer-events: none;
  animation: drift 16s ease-in-out infinite alternate;
}
.glow-a { width: 26rem; height: 26rem; background: #22a7f0; top: -8rem; left: -6rem; animation-delay: 0s; }
.glow-b { width: 22rem; height: 22rem; background: #3b5bf6; bottom: -9rem; right: -5rem; animation-delay: -5s; }
.glow-c { width: 18rem; height: 18rem; background: #38e0f0; bottom: 10%; left: 8%; opacity: 0.18; animation-delay: -10s; }

@keyframes drift {
  from { transform: translate(0, 0) scale(1); }
  to { transform: translate(2.5rem, 1.5rem) scale(1.08); }
}

.card {
  position: relative;
  z-index: 1;
  width: 360px;
  display: flex;
  flex-direction: column;
  gap: .5rem;
  background: color-mix(in srgb, var(--surface) 82%, transparent);
  backdrop-filter: blur(18px);
  -webkit-backdrop-filter: blur(18px);
  border: 1px solid var(--border);
  border-radius: 18px;
  padding: 2rem 1.75rem 1.75rem;
  box-shadow: 0 20px 60px -20px rgba(0, 0, 0, 0.55), 0 0 0 1px rgba(255, 255, 255, 0.02) inset;
}

.brand { display: flex; flex-direction: column; align-items: center; text-align: center; margin-bottom: .5rem; }
.mark { width: 52px; height: 52px; margin-bottom: .5rem; filter: drop-shadow(0 4px 14px rgba(34, 167, 240, 0.35)); }
.brand h1 {
  margin: 0;
  font-size: 1.5rem;
  font-weight: 700;
  background: linear-gradient(120deg, #38e0f0, #22a7f0 55%, #3b5bf6);
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
}
.brand p { margin: .2rem 0 0; font-size: .85rem; }

label { font-size: .8rem; color: var(--text-muted); margin-top: .4rem; }
.w-full { width: 100%; }
:deep(.p-password), :deep(.p-password-input), :deep(.p-inputtext) { width: 100%; }
:deep(.p-inputtext:focus), :deep(.p-password-input:focus) {
  box-shadow: 0 0 0 2px color-mix(in srgb, #22a7f0 45%, transparent);
  border-color: #22a7f0;
}

.submit-btn {
  background: linear-gradient(120deg, #22a7f0, #3b5bf6);
  border: none;
}
.submit-btn:hover { filter: brightness(1.08); }
</style>
