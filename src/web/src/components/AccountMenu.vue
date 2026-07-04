<script setup>
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'
import { useAuth } from '../stores/auth'
import Menu from 'primevue/menu'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import { useToast } from 'primevue/usetoast'

const { t } = useI18n()
const toast = useToast()
const auth = useAuth()

const menu = ref(null)
function toggleMenu(event) { menu.value.toggle(event) }

const showProfile = ref(false)
const showCredentials = ref(false)
const showAbout = ref(false)
const savingProfile = ref(false)
const savingCredentials = ref(false)
const version = ref('')

const profileForm = ref({ currentPassword: '', newUsername: '' })
const credForm = ref({ currentPassword: '', newPassword: '', confirmPassword: '' })

function openProfile() {
  profileForm.value = { currentPassword: '', newUsername: auth.user?.username || '' }
  showProfile.value = true
}
function openCredentials() {
  credForm.value = { currentPassword: '', newPassword: '', confirmPassword: '' }
  showCredentials.value = true
}

// A freshly-seeded admin account must change its password before using the
// app; block dismissal of the dialog until that succeeds.
const forceChangePassword = computed(() => auth.user?.mustChangePassword === true)
watch(() => auth.user?.mustChangePassword, (v) => { if (v) openCredentials() }, { immediate: true })
async function openAbout() {
  showAbout.value = true
  if (!version.value) {
    try { version.value = (await api.get('/version')).data.version } catch { version.value = '?' }
  }
}

const menuItems = computed(() => [
  { label: t('account.changeProfile'), icon: 'pi pi-user', command: openProfile },
  { label: t('account.changeCredentials'), icon: 'pi pi-lock', command: openCredentials },
  { label: t('account.about'), icon: 'pi pi-info-circle', command: openAbout },
])

function errorMessage(e, fallbackKey) {
  return e.response?.data?.error || t(fallbackKey)
}

async function submitProfile() {
  savingProfile.value = true
  try {
    const { data } = await api.put('/auth/profile', profileForm.value)
    auth.user.username = data.username
    toast.add({ severity: 'success', summary: t('account.profileUpdated'), life: 3000 })
    showProfile.value = false
  } catch (e) {
    toast.add({ severity: 'error', summary: errorMessage(e, 'account.updateFailed'), life: 4000 })
  } finally {
    savingProfile.value = false
  }
}

async function submitCredentials() {
  if (credForm.value.newPassword !== credForm.value.confirmPassword) {
    toast.add({ severity: 'error', summary: t('account.passwordMismatch'), life: 3000 })
    return
  }
  savingCredentials.value = true
  try {
    const { data } = await api.put('/auth/credentials', {
      currentPassword: credForm.value.currentPassword,
      newPassword: credForm.value.newPassword,
    })
    if (auth.user) auth.user.mustChangePassword = data.mustChangePassword
    toast.add({ severity: 'success', summary: t('account.credentialsUpdated'), life: 3000 })
    showCredentials.value = false
  } catch (e) {
    toast.add({ severity: 'error', summary: errorMessage(e, 'account.updateFailed'), life: 4000 })
  } finally {
    savingCredentials.value = false
  }
}
</script>

<template>
  <button class="account-trigger" @click="toggleMenu">
    {{ auth.user?.username }} <i class="pi pi-chevron-down" style="font-size:.7rem" />
  </button>
  <Menu ref="menu" :model="menuItems" popup />

  <Dialog v-model:visible="showProfile" :header="t('account.changeProfile')" modal :style="{ width: '24rem' }">
    <div class="field">
      <label>{{ t('account.currentPassword') }}</label>
      <Password v-model="profileForm.currentPassword" :feedback="false" toggleMask style="width:100%" />
    </div>
    <div class="field">
      <label>{{ t('account.newUsername') }}</label>
      <InputText v-model="profileForm.newUsername" style="width:100%" />
    </div>
    <template #footer>
      <Button :label="t('common.cancel')" text @click="showProfile = false" />
      <Button :label="t('common.save')" :loading="savingProfile" @click="submitProfile" />
    </template>
  </Dialog>

  <Dialog v-model:visible="showCredentials" :header="t('account.changeCredentials')" modal
    :closable="!forceChangePassword" :closeOnEscape="!forceChangePassword" :style="{ width: '24rem' }">
    <p v-if="forceChangePassword" class="muted" style="font-size:.85rem; margin-top:0">
      {{ t('account.mustChangePassword') }}
    </p>
    <div class="field">
      <label>{{ t('account.currentPassword') }}</label>
      <Password v-model="credForm.currentPassword" :feedback="false" toggleMask style="width:100%" />
    </div>
    <div class="field">
      <label>{{ t('account.newPassword') }}</label>
      <Password v-model="credForm.newPassword" :feedback="false" toggleMask style="width:100%" />
    </div>
    <div class="field">
      <label>{{ t('account.confirmPassword') }}</label>
      <Password v-model="credForm.confirmPassword" :feedback="false" toggleMask style="width:100%" />
    </div>
    <template #footer>
      <Button v-if="!forceChangePassword" :label="t('common.cancel')" text @click="showCredentials = false" />
      <Button :label="t('common.save')" :loading="savingCredentials" @click="submitCredentials" />
    </template>
  </Dialog>

  <Dialog v-model:visible="showAbout" :header="t('account.about')" modal :style="{ width: '26rem' }">
    <p><strong>Liquidcast</strong> {{ t('account.version') }} {{ version }}</p>
    <p class="muted" style="font-size:.82rem">
      <a href="https://github.com/pdirksen/liquidcast" target="_blank" rel="noopener">github.com/pdirksen/liquidcast</a>
    </p>
    <p class="muted" style="font-size:.85rem; margin-top:1rem">{{ t('account.license') }}</p>
    <pre class="license">MIT License

Copyright (c) 2026 Paul

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.</pre>
    <template #footer>
      <Button :label="t('common.cancel')" text @click="showAbout = false" />
    </template>
  </Dialog>
</template>

<style scoped>
.account-trigger {
  background: transparent; border: none; color: var(--text-muted); font-size: .9rem;
  display: flex; align-items: center; gap: .4rem; cursor: pointer; padding: .3rem .5rem;
  border-radius: 8px; margin-right: .25rem;
}
.account-trigger:hover { background: var(--surface-3); color: var(--text-strong); }
.field { margin-bottom: .85rem; }
.field label { display: block; font-size: .85rem; color: var(--text-muted); margin-bottom: .3rem; }
.license {
  white-space: pre-wrap; font-size: .72rem; line-height: 1.4; max-height: 12rem; overflow-y: auto;
  background: var(--surface-3); border-radius: 8px; padding: .6rem .75rem; margin-top: .4rem;
}
</style>
