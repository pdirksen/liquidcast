<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import draggable from 'vuedraggable'
import { api } from '../api/client'
import { fmtDuration } from '../util'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import { useToast } from 'primevue/usetoast'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const toast = useToast()
const id = Number(route.params.id)

const playlist = ref(null)
const items = ref([])
const library = ref([])
const search = ref('')
const saving = ref(false)

const filteredLibrary = computed(() => {
  const t = search.value.toLowerCase()
  return library.value.filter((x) =>
    !t || (x.title || '').toLowerCase().includes(t) || (x.artist || '').toLowerCase().includes(t) ||
    x.fileName.toLowerCase().includes(t))
})

const totalDuration = computed(() => items.value.reduce((s, i) => s + (i.durationSec || 0), 0))

let counter = 0
function cloneTrack(track) {
  return {
    uid: `n${counter++}`,
    trackId: track.id,
    title: track.title || track.fileName,
    artist: track.artist,
    durationSec: track.durationSec,
    cueInSec: null,
    cueOutSec: null,
    crossfadeSec: null,
  }
}

async function load() {
  const [pl, lib] = await Promise.all([
    api.get(`/playlists/${id}`),
    api.get('/tracks'),
  ])
  playlist.value = pl.data
  library.value = lib.data
  items.value = pl.data.items.map((it) => ({
    uid: `e${it.id}`,
    trackId: it.trackId,
    title: it.track?.title || it.track?.fileName,
    artist: it.track?.artist,
    durationSec: it.track?.durationSec || 0,
    cueInSec: it.cueInSec,
    cueOutSec: it.cueOutSec,
    crossfadeSec: it.crossfadeSec,
  }))
}
onMounted(load)

function removeItem(i) { items.value.splice(i, 1) }

async function save() {
  saving.value = true
  try {
    const payload = items.value.map((i) => ({
      trackId: i.trackId,
      cueInSec: i.cueInSec,
      cueOutSec: i.cueOutSec,
      crossfadeSec: i.crossfadeSec,
    }))
    await api.put(`/playlists/${id}/items`, payload)
    toast.add({ severity: 'success', summary: t('editor.saved'), life: 2000 })
  } catch {
    toast.add({ severity: 'error', summary: t('editor.saveFailed'), life: 3000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="page" v-if="playlist">
    <div class="row">
      <Button icon="pi pi-arrow-left" text @click="router.push('/playlists')" />
      <h1 style="margin:0">{{ playlist.name }}</h1>
      <span class="muted">· {{ items.length }} {{ t('editor.tracksWord') }} · {{ fmtDuration(totalDuration) }}</span>
      <span class="spacer" />
      <Button :label="t('editor.save')" icon="pi pi-save" :loading="saving" @click="save" />
    </div>

    <div class="editor mt">
      <!-- Library: drag source -->
      <div class="col">
        <div class="col-head">
          <span>{{ t('editor.library') }}</span>
          <InputText v-model="search" :placeholder="t('common.search')" size="small" />
        </div>
        <draggable :list="filteredLibrary" :group="{ name: 'tracks', pull: 'clone', put: false }"
          :clone="cloneTrack" item-key="id" :sort="false" class="list">
          <template #item="{ element }">
            <div class="lib-row">
              <i class="pi pi-bars handle" />
              <div class="meta">
                <div class="t">{{ element.title || element.fileName }}</div>
                <div class="a muted">{{ element.artist }}</div>
              </div>
              <span class="muted">{{ fmtDuration(element.durationSec) }}</span>
            </div>
          </template>
        </draggable>
      </div>

      <!-- Timeline: drop target + reorder -->
      <div class="col">
        <div class="col-head"><span>{{ t('editor.timeline') }}</span><span class="muted">{{ t('editor.dragReorder') }}</span></div>
        <draggable v-model="items" group="tracks" item-key="uid" class="list timeline" handle=".handle">
          <template #item="{ element, index }">
            <div class="tl-row">
              <span class="idx">{{ index + 1 }}</span>
              <i class="pi pi-bars handle" />
              <div class="meta">
                <div class="t">{{ element.title }}</div>
                <div class="a muted">{{ element.artist }} · {{ fmtDuration(element.durationSec) }}</div>
              </div>
              <div class="xf" v-tooltip.top="t('editor.crossfadeTip')">
                <InputNumber v-model="element.crossfadeSec" :min="0" :max="30" :step="0.5"
                  :minFractionDigits="0" :maxFractionDigits="1"
                  showButtons size="small" placeholder="xf" :inputStyle="{ width: '5rem' }" />
              </div>
              <Button icon="pi pi-times" text severity="danger" size="small" @click="removeItem(index)" />
            </div>
          </template>
          <template #footer>
            <div v-if="!items.length" class="empty muted">{{ t('editor.dragHint') }}</div>
          </template>
        </draggable>
      </div>
    </div>
  </div>
</template>

<style scoped>
.editor { display: grid; grid-template-columns: 1fr 1.3fr; gap: 1rem; align-items: start; }
.col { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; overflow: hidden; }
.col-head { display: flex; justify-content: space-between; align-items: center; padding: .7rem 1rem; border-bottom: 1px solid var(--border); font-weight: 600; }
.list { min-height: 300px; max-height: 65vh; overflow-y: auto; padding: .5rem; }
.timeline { background: var(--surface-2); }
.lib-row, .tl-row { display: flex; align-items: center; gap: .6rem; padding: .5rem .6rem; border-radius: 8px; }
.lib-row { background: var(--surface-2); margin-bottom: .35rem; cursor: grab; }
.tl-row { background: var(--surface-3); margin-bottom: .4rem; }
.handle { cursor: grab; color: var(--text-dim); }
.meta { flex: 1; min-width: 0; }
.t { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.a { font-size: .8rem; }
.idx { width: 1.4rem; text-align: right; color: var(--text-dim); font-variant-numeric: tabular-nums; }
.empty { padding: 2rem; text-align: center; }
@media (max-width: 850px) { .editor { grid-template-columns: 1fr; } }
</style>
