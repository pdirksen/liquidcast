<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import draggable from 'vuedraggable'
import { api } from '../api/client'
import { fmtDuration } from '../util'
import { usePreview } from '../composables/preview'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Paginator from 'primevue/paginator'
import { useToast } from 'primevue/usetoast'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const toast = useToast()
const preview = usePreview()
const id = Number(route.params.id)

const playlist = ref(null)
const items = ref([])
const library = ref([])
const search = ref('')
const sortDir = ref('asc')
const saving = ref(false)
// trackId -> { playlistIds, scheduledCount, archivedCount }; playlistId -> name (marker tooltips)
const usage = ref(new Map())
const playlistNames = ref(new Map())

function otherPlaylists(trackId) {
  const u = usage.value.get(trackId)
  if (!u) return []
  return u.playlistIds
    .filter((p) => p !== id)
    .map((p) => playlistNames.value.get(p) || `#${p}`)
}
function scheduledCount(trackId) {
  return usage.value.get(trackId)?.scheduledCount || 0
}
// Schedule entries past the archive window — counted apart from the live schedule.
function archivedCount(trackId) {
  return usage.value.get(trackId)?.archivedCount || 0
}

// Page the rendered rows — vuedraggable + thousands of DOM nodes is the perf sink, not the filter.
const first = ref(0)
const rows = ref(50)
const libName = (x) => x.title || x.fileName || ''
const matches = computed(() => {
  const t = search.value.toLowerCase()
  const dir = sortDir.value === 'desc' ? -1 : 1
  return library.value
    .filter((x) =>
      !t || (x.title || '').toLowerCase().includes(t) || (x.artist || '').toLowerCase().includes(t) ||
      x.fileName.toLowerCase().includes(t))
    .sort((a, b) => dir * libName(a).localeCompare(libName(b), undefined, { numeric: true, sensitivity: 'base' }))
})
const filteredLibrary = computed(() => matches.value.slice(first.value, first.value + rows.value))
// Filtering/sorting can shrink the result below the current page — jump back to page 1.
watch(matches, (m) => { if (first.value >= m.length) first.value = 0 })

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
  const [pl, lib, use, all] = await Promise.all([
    api.get(`/playlists/${id}`),
    api.get('/tracks'),
    api.get('/tracks/usage'),
    api.get('/playlists'),
  ])
  playlist.value = pl.data
  library.value = lib.data
  usage.value = new Map(use.data.map((u) => [u.trackId, u]))
  playlistNames.value = new Map(all.data.map((p) => [p.id, p.name]))
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

async function playPreview(trackId) {
  if (!await preview.toggle(trackId))
    toast.add({ severity: 'error', summary: t('tracks.previewFailed'), life: 3000 })
}

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
          <span class="head-tools">
            <InputText v-model="search" :placeholder="t('common.search')" size="small" />
            <Button text size="small" :icon="sortDir === 'asc' ? 'pi pi-sort-alpha-down' : 'pi pi-sort-alpha-up-alt'"
              v-tooltip.top="sortDir === 'asc' ? t('editor.sortDesc') : t('editor.sortAsc')"
              @click="sortDir = sortDir === 'asc' ? 'desc' : 'asc'" />
          </span>
        </div>
        <div class="legend muted">
          <span><i class="pi pi-list mark mark-pl" /> {{ t('editor.legendPlaylist') }}</span>
          <span><i class="pi pi-calendar mark mark-sched" /> {{ t('editor.legendSchedule') }}</span>
          <span><i class="pi pi-inbox mark mark-arch" /> {{ t('editor.legendArchived') }}</span>
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
              <Button text size="small" class="play"
                :icon="preview.isLoading(element.id) ? 'pi pi-spin pi-spinner'
                  : preview.isPlaying(element.id) ? 'pi pi-pause' : 'pi pi-play'"
                v-tooltip.top="preview.isPlaying(element.id) ? t('tracks.pause') : t('tracks.play')"
                @click.stop="playPreview(element.id)" />
              <Button v-if="preview.isActive(element.id)" icon="pi pi-forward" text size="small"
                class="play" v-tooltip.top="t('tracks.skip10')" @click.stop="preview.skip(10)" />
              <i v-if="otherPlaylists(element.id).length" class="pi pi-list mark mark-pl"
                v-tooltip.top="t('editor.usedInPlaylists', {
                  count: otherPlaylists(element.id).length,
                  names: otherPlaylists(element.id).join(', '),
                })" />
              <i v-if="scheduledCount(element.id)" class="pi pi-calendar mark mark-sched"
                v-tooltip.top="t('editor.usedInSchedule', { count: scheduledCount(element.id) })" />
              <i v-if="archivedCount(element.id)" class="pi pi-inbox mark mark-arch"
                v-tooltip.top="t('editor.usedInArchive', { count: archivedCount(element.id) })" />
              <span class="muted">{{ fmtDuration(element.durationSec) }}</span>
            </div>
          </template>
        </draggable>
        <Paginator v-model:first="first" v-model:rows="rows" :totalRecords="matches.length"
          :rowsPerPageOptions="[25, 50, 100, 250]" class="lib-pager"
          template="PrevPageLink PageLinks NextPageLink RowsPerPageDropdown">
          <template #start>
            <span class="muted pager-report">{{ t('editor.libraryPageReport', {
              first: matches.length ? first + 1 : 0,
              last: Math.min(first + rows, matches.length),
              total: matches.length,
            }) }}</span>
          </template>
        </Paginator>
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
              <Button text size="small" class="play"
                :icon="preview.isLoading(element.trackId) ? 'pi pi-spin pi-spinner'
                  : preview.isPlaying(element.trackId) ? 'pi pi-pause' : 'pi pi-play'"
                v-tooltip.top="preview.isPlaying(element.trackId) ? t('tracks.pause') : t('tracks.play')"
                @click="playPreview(element.trackId)" />
              <Button v-if="preview.isActive(element.trackId)" icon="pi pi-forward" text size="small"
                class="play" v-tooltip.top="t('tracks.skip10')" @click="preview.skip(10)" />
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
.play { flex: none; width: 2rem; height: 2rem; }
.mark { font-size: .8rem; flex: none; }
.mark-pl { color: var(--accent); }
.mark-sched { color: var(--warn); }
.mark-arch { color: var(--text-dim); }
.head-tools { display: flex; align-items: center; gap: .35rem; }
.legend { display: flex; gap: 1rem; padding: .4rem 1rem; font-size: .78rem; border-bottom: 1px solid var(--border); }
.legend span { display: inline-flex; align-items: center; gap: .3rem; }
.meta { flex: 1; min-width: 0; }
.t { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.a { font-size: .8rem; }
.idx { width: 1.4rem; text-align: right; color: var(--text-dim); font-variant-numeric: tabular-nums; }
.empty { padding: 2rem; text-align: center; }
.lib-pager { border-top: 1px solid var(--border); flex-wrap: wrap; }
.pager-report { font-size: .82rem; }
@media (max-width: 850px) { .editor { grid-template-columns: 1fr; } }
</style>
