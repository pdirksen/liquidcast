<script setup>
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'
import { usePrefs, DATE_FORMATS } from '../stores/prefs'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Password from 'primevue/password'
import Select from 'primevue/select'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import BackupPanel from '../components/BackupPanel.vue'
import { useToast } from 'primevue/usetoast'

const { t } = useI18n()
const toast = useToast()
const prefs = usePrefs()
const dateFormats = DATE_FORMATS
const s = ref(null)
const playlists = ref([])
const saving = ref(false)
const tab = ref('stream')

// Standard Shoutcast/Icecast directory genres, sent verbatim in the ICY genre header.
const GENRES = [
  'Various', 'Alternative', 'Ambient', 'Blues', 'Chillout', 'Classical', 'Country',
  'Dance', 'Disco', 'Downtempo', 'Drum & Bass', 'Easy Listening', 'Electronic', 'Folk',
  'Funk', 'Gospel', 'Hard Rock', 'Hip Hop', 'House', 'Indie', 'Jazz', 'Latin', 'Lounge',
  'Metal', 'News', 'Oldies', 'Pop', 'Punk', 'R&B', 'Reggae', 'Rock', 'Salsa', 'Ska',
  'Soul', 'Sports', 'Talk', 'Techno', 'Trance', 'World',
].map((g) => ({ label: g, value: g }))

const fallbackModes = computed(() => [
  { label: t('settings.silence'), value: 0 },
  { label: t('settings.fallbackPlaylistOpt'), value: 1 },
])
const controlModes = computed(() => [
  { label: t('settings.tcpMode'), value: 0 },
  { label: t('settings.unixMode'), value: 1 },
])

async function load() {
  const [cfg, pl] = await Promise.all([api.get('/settings'), api.get('/playlists')])
  s.value = cfg.data
  playlists.value = [{ label: t('common.none'), value: null }, ...pl.data.map((x) => ({ label: x.name, value: x.id }))]
}
onMounted(load)

async function save() {
  saving.value = true
  try {
    await api.put('/settings', s.value)
    toast.add({ severity: 'success', summary: t('settings.saved'), detail: t('settings.restarting'), life: 3000 })
  } catch {
    toast.add({ severity: 'error', summary: t('settings.saveFailed'), life: 3000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="page" v-if="s">
    <div class="row">
      <h1 style="margin:0">{{ t('settings.title') }}</h1>
      <span class="spacer" />
      <Button v-if="tab !== 'backup'" :label="t('settings.saveApply')" icon="pi pi-save"
        :loading="saving" @click="save" />
    </div>
    <Message v-if="tab !== 'backup'" severity="warn" :closable="false" class="mt">
      {{ t('settings.warn') }}
    </Message>

    <Tabs v-model:value="tab" class="mt">
      <TabList>
        <Tab value="stream">{{ t('settings.tabStream') }}</Tab>
        <Tab value="playback">{{ t('settings.tabPlayback') }}</Tab>
        <Tab value="system">{{ t('settings.tabSystem') }}</Tab>
        <Tab value="backup">{{ t('settings.tabBackup') }}</Tab>
      </TabList>
      <TabPanels>
        <TabPanel value="stream">
          <div class="cards">
            <section class="card">
              <h3>{{ t('settings.icecastOutput') }}</h3>
              <div class="grid2">
                <label>{{ t('settings.host') }}</label><InputText v-model="s.icecastHost" />
                <label>{{ t('settings.port') }}</label><InputNumber v-model="s.icecastPort" :useGrouping="false" />
                <label>{{ t('settings.mount') }}</label><InputText v-model="s.icecastMount" />
                <label>{{ t('settings.sourcePassword') }}</label><Password v-model="s.icecastPassword" :feedback="false" toggleMask />
                <label>{{ t('settings.bitrate') }}</label><InputNumber v-model="s.bitrate" :useGrouping="false" />
                <label>{{ t('settings.streamName') }}</label><InputText v-model="s.streamName" />
                <label>{{ t('settings.description') }}</label><InputText v-model="s.streamDescription" />
                <label>{{ t('settings.genre') }}</label>
                <Select v-model="s.genre" :options="GENRES" optionLabel="label" optionValue="value"
                  filter :filterPlaceholder="t('common.search')" editable />
                <label>{{ t('settings.publicStreamUrl') }}</label>
                <InputText v-model="s.publicStreamUrl" placeholder="https://radio.example.com/stream" />
              </div>
              <p class="muted" style="font-size:.82rem">{{ t('settings.publicStreamUrlHint') }}</p>
            </section>
            <section class="card">
              <h3>{{ t('settings.icecastAdmin') }}</h3>
              <div class="grid2">
                <label>{{ t('settings.adminUser') }}</label><InputText v-model="s.icecastAdminUser" />
                <label>{{ t('settings.adminPassword') }}</label><Password v-model="s.icecastAdminPassword" :feedback="false" toggleMask />
              </div>
            </section>
          </div>
        </TabPanel>

        <TabPanel value="playback">
          <div class="cards">
            <section class="card">
              <h3>{{ t('settings.crossfade') }}</h3>
              <div class="grid2">
                <label>{{ t('settings.defaultCrossfade') }}</label><InputNumber v-model="s.defaultCrossfadeSec" :minFractionDigits="1" />
                <label>{{ t('settings.fadeIn') }}</label><InputNumber v-model="s.fadeInSec" :minFractionDigits="1" />
                <label>{{ t('settings.fadeOut') }}</label><InputNumber v-model="s.fadeOutSec" :minFractionDigits="1" />
              </div>
            </section>
            <section class="card">
              <h3>{{ t('settings.fallback') }}</h3>
              <div class="grid2">
                <label>{{ t('settings.whenQueueEmpty') }}</label>
                <Select v-model="s.fallbackMode" :options="fallbackModes" optionLabel="label" optionValue="value" />
                <label v-if="s.fallbackMode === 1">{{ t('settings.fallbackPlaylistLabel') }}</label>
                <Select v-if="s.fallbackMode === 1" v-model="s.fallbackPlaylistId" :options="playlists"
                  optionLabel="label" optionValue="value" />
              </div>
              <p class="muted" style="font-size:.82rem">{{ t('settings.fallbackNote', { dir: 'data/fallback' }) }}</p>
            </section>
          </div>
        </TabPanel>

        <TabPanel value="system">
          <div class="cards">
            <section class="card">
              <h3>{{ t('settings.lsProcess') }}</h3>
              <div class="grid2">
                <label>{{ t('settings.binaryPath') }}</label><InputText v-model="s.liquidsoapPath" :placeholder="t('settings.fromPath')" />
                <label>{{ t('settings.controlMode') }}</label>
                <Select v-model="s.controlMode" :options="controlModes" optionLabel="label" optionValue="value" />
                <label>{{ t('settings.telnetPort') }}</label><InputNumber v-model="s.telnetPort" :useGrouping="false" />
                <label>{{ t('settings.lsLogLevel') }}</label><InputNumber v-model="s.liquidsoapLogLevel" :min="1" :max="4" :useGrouping="false" showButtons />
                <label>{{ t('settings.dataPath') }}</label><InputText v-model="s.dataPath" />
              </div>
            </section>
            <section class="card">
              <h3>{{ t('settings.display') }}</h3>
              <div class="grid2">
                <label>{{ t('settings.dateFormat') }}</label>
                <Select :modelValue="prefs.dateFormat" @update:modelValue="prefs.setDateFormat"
                  :options="dateFormats" optionLabel="label" optionValue="code" />
              </div>
            </section>
            <section class="card">
              <h3>{{ t('settings.uploadsSecurity') }}</h3>
              <div class="grid2">
                <label>{{ t('settings.maxUploadSizeMb') }}</label>
                <InputNumber v-model="s.maxUploadSizeMb" :min="1" :useGrouping="false" />
                <label>{{ t('settings.loginRateLimitPermit') }}</label>
                <InputNumber v-model="s.loginRateLimitPermitLimit" :min="1" :useGrouping="false" />
                <label>{{ t('settings.loginRateLimitWindowSec') }}</label>
                <InputNumber v-model="s.loginRateLimitWindowSec" :min="1" :useGrouping="false" />
              </div>
            </section>
          </div>
        </TabPanel>

        <TabPanel value="backup">
          <BackupPanel />
        </TabPanel>
      </TabPanels>
    </Tabs>
  </div>
</template>

<style scoped>
.cards { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; align-items: start; }
.card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 1rem 1.2rem; }
.card h3 { margin: 0 0 .75rem; font-size: 1rem; }
.grid2 { display: grid; grid-template-columns: 11rem 1fr; gap: .55rem; align-items: center; }
.grid2 label { font-size: .85rem; color: var(--text-muted); }
:deep(.p-inputtext), :deep(.p-password), :deep(.p-select), :deep(.p-inputnumber) { width: 100%; }
@media (max-width: 850px) { .cards { grid-template-columns: 1fr; } }
</style>
