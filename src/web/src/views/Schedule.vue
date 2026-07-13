<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'
import { datePickerProps } from '../util'
import { usePrefs } from '../stores/prefs'
import { classifyOverlap } from '../components/schedule/scheduleMath'
import TrackPanel from '../components/schedule/TrackPanel.vue'
import TimelineGrid from '../components/schedule/TimelineGrid.vue'
import EntryDialog from '../components/schedule/EntryDialog.vue'
import Button from 'primevue/button'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'

const { t } = useI18n()
const prefs = usePrefs()
const toast = useToast()
const confirm = useConfirm()
const dpProps = computed(() => datePickerProps(prefs.dateFormat))

const atMidnight = (d) => { const x = new Date(d); x.setHours(0, 0, 0, 0); return x }
const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x }

const day = ref(atMidnight(new Date()))
const zoom = ref(Number(localStorage.getItem('scheduleZoom')) || 5)
const zoomOptions = computed(() => [
  { label: t('schedule.zoomFit'), value: 2 },
  { label: t('schedule.zoomMedium'), value: 5 },
  { label: t('schedule.zoomDetail'), value: 10 },
])
watch(zoom, (v) => localStorage.setItem('scheduleZoom', String(v)))

const entries = ref([])
const dragPayload = ref(null)

// DatePicker binds a plain date; keep `day` normalized to local midnight.
const pickerDay = computed({
  get: () => day.value,
  set: (v) => { if (v) day.value = atMidnight(v) },
})
const isToday = computed(() => day.value.getTime() === atMidnight(new Date()).getTime())

async function load() {
  // ±1 day margin: midnight-spanning entries render and client overlap checks stay correct.
  const from = addDays(day.value, -1).toISOString()
  const to = addDays(day.value, 2).toISOString()
  const res = await api.get('/schedule', { params: { from, to } })
  entries.value = res.data
}
onMounted(load)
watch(day, load)

// --- create / edit ----------------------------------------------------------
const showDialog = ref(false)
const draft = ref(null)

function draftBody() {
  return {
    trackId: draft.value.trackId,
    line: draft.value.line,
    startUtc: new Date(draft.value.start).toISOString(),
    override: draft.value.override,
    cueInSec: draft.value.cueInSec,
    cueOutSec: draft.value.cueOutSec,
    crossfadeSec: draft.value.crossfadeSec,
  }
}

async function onDropTrack({ payload, line, start }) {
  const startMs = start.getTime()
  const endMs = startMs + (payload.effDurationSec || 0) * 1000
  const { sameLine, needsOverride } = classifyOverlap(entries.value, { id: null, line, startMs, endMs })

  draft.value = {
    id: null,
    trackId: payload.trackId,
    title: payload.title,
    artist: payload.artist,
    effDurationSec: payload.effDurationSec,
    line,
    start,
    override: false,
    cueInSec: payload.cueInSec,
    cueOutSec: payload.cueOutSec,
    crossfadeSec: payload.crossfadeSec,
  }

  if (sameLine || needsOverride) {
    // Needs a decision (move it, or confirm override) → open the dialog.
    showDialog.value = true
    return
  }
  await saveDraft()
}

// Entry dragged to a new position/line inside the grid.
async function onMoveEntry({ payload, line, start }) {
  const startMs = start.getTime()
  const endMs = startMs + (payload.effDurationSec || 0) * 1000
  const { sameLine, needsOverride } = classifyOverlap(entries.value,
    { id: payload.entryId, line, startMs, endMs })

  draft.value = {
    id: payload.entryId,
    trackId: payload.trackId,
    title: payload.title,
    artist: payload.artist,
    effDurationSec: payload.effDurationSec,
    line,
    start,
    override: payload.override,
    cueInSec: payload.cueInSec,
    cueOutSec: payload.cueOutSec,
    crossfadeSec: payload.crossfadeSec,
  }

  if (sameLine || (needsOverride && !payload.override)) {
    showDialog.value = true
    return
  }
  await saveDraft()
}

function onEdit(entry) {
  draft.value = {
    id: entry.id,
    trackId: entry.trackId,
    title: entry.title,
    artist: entry.artist,
    effDurationSec: entry.durationSec,
    line: entry.line,
    start: new Date(entry.startUtc),
    override: entry.override,
    cueInSec: entry.cueInSec,
    cueOutSec: entry.cueOutSec,
    crossfadeSec: entry.crossfadeSec,
  }
  showDialog.value = true
}

async function saveDraft() {
  try {
    if (draft.value.id) await api.put(`/schedule/${draft.value.id}`, draftBody())
    else await api.post('/schedule', draftBody())
    showDialog.value = false
    toast.add({ severity: 'success', summary: t('schedule.saved'), life: 2000 })
    await load()
  } catch (e) {
    const code = e.response?.data?.code
    const detail = code === 'same-line' ? t('schedule.overlapSameLine')
      : code === 'needs-override' ? t('schedule.overlapNeedsOverride')
      : e.response?.data?.error
    toast.add({ severity: 'error', summary: t('schedule.cannotSave'), detail, life: 4000 })
    if (!showDialog.value) showDialog.value = true // let the user adjust the drop
  }
}

function onRemove(id) {
  confirm.require({
    message: t('schedule.deleteMsg'),
    header: t('schedule.deleteHeader'), icon: 'pi pi-trash',
    acceptProps: { severity: 'danger', label: t('common.delete') },
    accept: async () => {
      await api.delete(`/schedule/${id}`)
      showDialog.value = false
      await load()
    },
  })
}
</script>

<template>
  <div class="page">
    <h1>{{ t('schedule.title') }}</h1>

    <div class="toolbar">
      <Button icon="pi pi-chevron-left" text @click="day = addDays(day, -1)" />
      <DatePicker v-model="pickerDay" :dateFormat="dpProps.dateFormat" showIcon iconDisplay="input"
        class="daypick" />
      <Button icon="pi pi-chevron-right" text @click="day = addDays(day, 1)" />
      <Button :label="t('schedule.today')" size="small" :severity="isToday ? 'primary' : 'secondary'"
        @click="day = atMidnight(new Date())" />
      <span class="spacer" />
      <span class="muted">{{ t('schedule.zoom') }}</span>
      <Select v-model="zoom" :options="zoomOptions" optionLabel="label" optionValue="value" size="small" />
    </div>

    <div class="layout mt">
      <TimelineGrid :day="day" :zoom="zoom" :entries="entries" :dragPayload="dragPayload"
        @drop-track="onDropTrack" @move-entry="onMoveEntry" @edit="onEdit"
        @drag-start="dragPayload = $event" @drag-end="dragPayload = null" />
      <TrackPanel @drag-start="dragPayload = $event" @drag-end="dragPayload = null" />
    </div>
    <div class="muted hint">{{ t('schedule.dropHint') }}</div>

    <EntryDialog v-model:visible="showDialog" :draft="draft" :entries="entries"
      @save="saveDraft" @remove="onRemove" />
  </div>
</template>

<style scoped>
.toolbar { display: flex; align-items: center; gap: .5rem; }
.daypick { width: 11rem; }
.layout { display: grid; grid-template-columns: 1fr 300px; gap: 1rem; align-items: stretch; }
.hint { margin-top: .5rem; font-size: .8rem; }
@media (max-width: 1000px) { .layout { grid-template-columns: 1fr; } }
</style>
