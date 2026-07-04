<script setup>
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'
import { formatDateTime, fmtBytes } from '../util'
import { usePrefs } from '../stores/prefs'
import ToggleSwitch from 'primevue/toggleswitch'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import Button from 'primevue/button'
import FileUpload from 'primevue/fileupload'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const prefs = usePrefs()

const cfg = ref(null)
const files = ref([])
const selected = ref(null)
const saving = ref(false)
const running = ref(false)
const restoring = ref(false)

const options = computed(() => files.value.map((f) => ({
  label: `${f.name}  ·  ${fmtBytes(f.size)}`, value: f.name,
})))

async function loadFiles() { files.value = (await api.get('/backup/list')).data }

async function load() {
  cfg.value = (await api.get('/backup')).data
  await loadFiles()
}
onMounted(load)

async function save() {
  saving.value = true
  try {
    cfg.value = (await api.put('/backup', cfg.value)).data
    toast.add({ severity: 'success', summary: t('backup.saved'), life: 2000 })
  } finally {
    saving.value = false
  }
}

async function runNow() {
  running.value = true
  try {
    const { name } = (await api.post('/backup/run')).data
    // Trigger download (cookie auth, same origin).
    const a = document.createElement('a')
    a.href = `/api/backup/download?name=${encodeURIComponent(name)}`
    document.body.appendChild(a); a.click(); a.remove()
    toast.add({ severity: 'success', summary: t('backup.runSuccess'), detail: name, life: 4000 })
    cfg.value = (await api.get('/backup')).data
    await loadFiles()
  } catch {
    toast.add({ severity: 'error', summary: t('backup.runFailed'), life: 4000 })
  } finally {
    running.value = false
  }
}

function confirmRestore(action) {
  confirm.require({
    message: t('backup.confirm'),
    header: t('backup.restore'),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { severity: 'danger', label: t('backup.restore') },
    accept: async () => {
      restoring.value = true
      try {
        await action()
        toast.add({ severity: 'success', summary: t('backup.restoreSuccess'), life: 3000 })
        await load()
      } catch (e) {
        toast.add({ severity: 'error', summary: t('backup.restoreFailed'), detail: e.response?.data?.detail, life: 5000 })
      } finally {
        restoring.value = false
      }
    },
  })
}

function restoreSelected() {
  if (!selected.value) return
  confirmRestore(() => api.post('/backup/restore-file', { name: selected.value }))
}

function restoreFromFile(event) {
  const file = event.files?.[0]
  if (!file) return
  confirmRestore(() => {
    const form = new FormData()
    form.append('file', file)
    return api.post('/backup/restore', form)
  })
}

const lastRun = computed(() =>
  cfg.value?.lastBackupAt ? formatDateTime(cfg.value.lastBackupAt, prefs.dateFormat) : t('backup.never'))
</script>

<template>
  <div v-if="cfg" class="cards">
    <section class="card">
      <div class="row">
        <h3 style="margin:0">{{ t('backup.schedule') }}</h3>
        <span class="spacer" />
        <Button :label="t('common.save')" icon="pi pi-save" size="small" :loading="saving" @click="save" />
      </div>
      <div class="grid2 mt">
        <label>{{ t('backup.enabled') }}</label>
        <ToggleSwitch v-model="cfg.enabled" />
        <label>{{ t('backup.targetPath') }}</label>
        <InputText v-model="cfg.targetPath" />
        <label>{{ t('backup.time') }}</label>
        <InputText v-model="cfg.scheduleTime" placeholder="HH:MM" />
        <label>{{ t('backup.keep') }}</label>
        <InputNumber v-model="cfg.keepCount" :min="1" :useGrouping="false" showButtons />
      </div>
      <p class="muted" style="font-size:.82rem">{{ t('backup.scheduleHint') }}</p>
      <p class="muted" style="font-size:.82rem">{{ t('backup.lastRun') }}: {{ lastRun }}</p>
    </section>

    <section class="card">
      <h3>{{ t('backup.manual') }}</h3>
      <Button :label="t('backup.runNow')" icon="pi pi-download" :loading="running" @click="runNow" />

      <h3 class="mt">{{ t('backup.restore') }}</h3>
      <p class="muted" style="font-size:.82rem">{{ t('backup.restoreHint') }}</p>
      <div class="grid2">
        <label>{{ t('backup.fromList') }}</label>
        <div class="row">
          <Select v-model="selected" :options="options" optionLabel="label" optionValue="value"
            :placeholder="t('backup.select')" :emptyMessage="t('backup.none')" showClear class="grow" />
          <Button icon="pi pi-replay" :label="t('backup.restore')" :loading="restoring"
            :disabled="!selected" @click="restoreSelected" />
        </div>
        <label>{{ t('backup.fromFile') }}</label>
        <FileUpload mode="basic" accept=".zip,application/zip" :auto="true" :customUpload="true"
          :chooseLabel="t('backup.choose')" :disabled="restoring" @uploader="restoreFromFile" />
      </div>
    </section>
  </div>
</template>

<style scoped>
.cards { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; align-items: start; }
.card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 1rem 1.2rem; }
.card h3 { margin: 0 0 .75rem; font-size: 1rem; }
.grid2 { display: grid; grid-template-columns: 11rem 1fr; gap: .55rem; align-items: center; }
.grid2 label { font-size: .85rem; color: var(--text-muted); }
.grow { flex: 1; }
:deep(.p-inputtext), :deep(.p-select), :deep(.p-inputnumber) { width: 100%; }
@media (max-width: 850px) { .cards { grid-template-columns: 1fr; } }
</style>
