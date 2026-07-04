<script setup>
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuth } from '../stores/auth'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'

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
    <form class="card" @submit.prevent="submit">
      <div class="logo"><i class="pi pi-wifi" /> Liquidcast</div>
      <p class="muted">{{ t('login.subtitle') }}</p>
      <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
      <label>{{ t('login.username') }}</label>
      <InputText v-model="username" autofocus />
      <label>{{ t('login.password') }}</label>
      <Password v-model="password" :feedback="false" toggleMask inputClass="w-full" />
      <Button type="submit" :loading="busy" :label="t('login.signIn')" class="mt" />
    </form>
  </div>
</template>

<style scoped>
.login-wrap { display: grid; place-items: center; min-height: 100vh; }
.card {
  width: 340px; display: flex; flex-direction: column; gap: .5rem;
  background: var(--surface); border: 1px solid var(--border); border-radius: 14px; padding: 1.5rem;
}
.logo { font-size: 1.4rem; font-weight: 700; display: flex; gap: .5rem; align-items: center; }
.logo .pi { color: var(--accent); }
label { font-size: .8rem; color: var(--text-muted); margin-top: .4rem; }
:deep(.p-password), :deep(.p-password-input), :deep(.p-inputtext) { width: 100%; }
</style>
