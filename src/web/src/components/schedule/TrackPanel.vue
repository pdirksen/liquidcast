<script setup>
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../../api/client'
import { fmtDuration } from '../../util'
import { effDurationSec } from './scheduleMath'
import Select from 'primevue/select'
import InputText from 'primevue/inputtext'

const { t } = useI18n()
const emit = defineEmits(['drag-start', 'drag-end'])

const playlists = ref([])
const playlistId = ref(null)
const tracks = ref([])
const search = ref('')

const filtered = computed(() => {
  const q = search.value.toLowerCase()
  return tracks.value.filter((x) =>
    !q || (x.title || '').toLowerCase().includes(q) || (x.artist || '').toLowerCase().includes(q))
})

onMounted(async () => {
  const res = await api.get('/playlists')
  playlists.value = res.data.map((p) => ({ label: p.name, value: p.id }))
})

async function loadTracks() {
  if (!playlistId.value) { tracks.value = []; return }
  const res = await api.get(`/playlists/${playlistId.value}`)
  tracks.value = (res.data.items || []).map((it) => ({
    trackId: it.trackId,
    title: it.track?.title || it.track?.fileName,
    artist: it.track?.artist,
    durationSec: it.track?.durationSec || 0,
    cueInSec: it.cueInSec,
    cueOutSec: it.cueOutSec,
    crossfadeSec: it.crossfadeSec,
  }))
}

function onDragStart(e, track) {
  const payload = { ...track, effDurationSec: effDurationSec(track) }
  e.dataTransfer.effectAllowed = 'copy'
  e.dataTransfer.setData('application/x-lc-track', JSON.stringify(payload))
  emit('drag-start', payload)
}
</script>

<template>
  <div class="panel">
    <div class="panel-head">
      <span>{{ t('schedule.tracks') }}</span>
    </div>
    <div class="panel-tools">
      <Select v-model="playlistId" :options="playlists" optionLabel="label" optionValue="value"
        :placeholder="t('schedule.selectPlaylist')" filter :filterPlaceholder="t('common.search')"
        size="small" fluid @change="loadTracks" />
      <InputText v-if="tracks.length" v-model="search" :placeholder="t('common.search')" size="small" fluid />
    </div>
    <div class="list">
      <div v-for="(tr, i) in filtered" :key="i" class="lib-row" draggable="true"
        @dragstart="onDragStart($event, tr)" @dragend="emit('drag-end')">
        <i class="pi pi-bars handle" />
        <div class="meta">
          <div class="t">{{ tr.title }}</div>
          <div class="a muted">{{ tr.artist }}</div>
        </div>
        <span class="muted">{{ fmtDuration(effDurationSec(tr)) }}</span>
      </div>
      <div v-if="!playlistId" class="empty muted">{{ t('schedule.pickPlaylistHint') }}</div>
      <div v-else-if="!filtered.length" class="empty muted">{{ t('schedule.noTracks') }}</div>
    </div>
  </div>
</template>

<style scoped>
.panel { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; overflow: hidden; display: flex; flex-direction: column; }
.panel-head { padding: .7rem 1rem; border-bottom: 1px solid var(--border); font-weight: 600; }
.panel-tools { display: flex; flex-direction: column; gap: .5rem; padding: .6rem; border-bottom: 1px solid var(--border); }
.list { flex: 1; min-height: 200px; overflow-y: auto; padding: .5rem; }
.lib-row { display: flex; align-items: center; gap: .6rem; padding: .5rem .6rem; border-radius: 8px; background: var(--surface-2); margin-bottom: .35rem; cursor: grab; }
.lib-row:active { cursor: grabbing; }
.handle { cursor: grab; color: var(--text-dim); }
.meta { flex: 1; min-width: 0; }
.t { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.a { font-size: .8rem; }
.empty { padding: 2rem 1rem; text-align: center; }
</style>
