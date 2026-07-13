<script setup>
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { classifyOverlap, LINE_ORDER } from './scheduleMath'
import { useLineNames } from '../../composables/lineNames'

const props = defineProps({
  day: { type: Date, required: true },        // local midnight of the shown day
  zoom: { type: Number, required: true },     // px per minute
  entries: { type: Array, required: true },   // schedule window (may span beyond the day)
  dragPayload: { type: Object, default: null },
})
const emit = defineEmits(['drop-track', 'move-entry', 'edit', 'drag-start', 'drag-end'])
const { t } = useI18n()

const DAY_MIN = 24 * 60
const RULER_H = 28
const LANE_H = 56

const dayStartMs = computed(() => props.day.getTime())
const canvasW = computed(() => DAY_MIN * props.zoom)
const hourW = computed(() => 60 * props.zoom)
const snapMin = computed(() => (props.zoom >= 5 ? 1 : 5))

const { lineLabel, renameLine } = useLineNames()

const lanes = computed(() => LINE_ORDER.map((line) => ({ line, label: lineLabel(line) })))

// --- rename a line by clicking its label -------------------------------------
const editingLine = ref(null)
const editName = ref('')
const editEl = ref(null)
function setEditEl(el) { editEl.value = el }

function startEdit(line) {
  editingLine.value = line
  editName.value = lineLabel(line)
  nextTick(() => { editEl.value?.focus(); editEl.value?.select() })
}

async function commitEdit() {
  if (editingLine.value == null) return // Enter already committed; this is the trailing blur
  const line = editingLine.value
  editingLine.value = null
  try { await renameLine(line, editName.value) } catch { /* keep the previous name */ }
}

function cancelEdit() { editingLine.value = null }

const hh = (h) => `${String(h).padStart(2, '0')}:00`
const hhmm = (iso) => {
  const d = new Date(iso)
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

function xOf(ms) { return ((ms - dayStartMs.value) / 60000) * props.zoom }

function laneEntries(line) {
  const dayEnd = dayStartMs.value + DAY_MIN * 60000
  return props.entries.filter((e) => {
    if (e.line !== line) return false
    const s = new Date(e.startUtc).getTime()
    const en = new Date(e.endUtc).getTime()
    return en > dayStartMs.value && s < dayEnd
  })
}

function entryStyle(e) {
  const s = new Date(e.startUtc).getTime()
  const en = new Date(e.endUtc).getTime()
  const left = Math.max(0, xOf(s))
  const right = Math.min(canvasW.value, xOf(en))
  return { left: `${left}px`, width: `${Math.max(6, right - left)}px` }
}

// --- drag & drop -----------------------------------------------------------
// dataTransfer payloads are unreadable during dragover, so the ghost preview
// relies on dragPayload (set by TrackPanel while a drag is in flight).
const ghost = ref(null) // { line, startMs }

const EDGE_SNAP_PX = 12

// Magnet: near another entry's edge in the same lane, snap this block's start
// to that entry's end (or its end to that entry's start) at full precision —
// tracks are second-accurate, so this must beat the minute grid.
function edgeSnapMs(rawMs, line, durSec, excludeId) {
  const durMs = (durSec || 0) * 1000
  const tolMs = (EDGE_SNAP_PX / props.zoom) * 60000
  let best = null
  for (const en of props.entries) {
    if (en.line !== line || en.id === excludeId) continue
    const s = new Date(en.startUtc).getTime()
    const en2 = new Date(en.endUtc).getTime()
    for (const c of [
      { ms: en2, dist: Math.abs(rawMs - en2) },              // start where it ends
      { ms: s - durMs, dist: Math.abs(rawMs + durMs - s) },  // end where it starts
    ]) {
      if (c.dist <= tolMs && (!best || c.dist < best.dist)) best = c
    }
  }
  return best ? best.ms : null
}

// offsetMin: where inside the block the user grabbed it, so a moved entry
// lands where its START is, not where the pointer is.
function startMsAt(e, laneEl, line, payload) {
  const rect = laneEl.getBoundingClientRect()
  const rawMin = (e.clientX - rect.left) / props.zoom - (payload?.grabOffsetMin || 0)
  const snapped = edgeSnapMs(dayStartMs.value + rawMin * 60000, line,
    payload?.effDurationSec, payload?.entryId ?? null)
  if (snapped != null) return snapped
  const min = Math.round(rawMin / snapMin.value) * snapMin.value
  return dayStartMs.value + Math.min(DAY_MIN, Math.max(0, min)) * 60000
}

function onDragOver(e, line) {
  if (!props.dragPayload) return
  e.dataTransfer.dropEffect = props.dragPayload.entryId ? 'move' : 'copy'
  ghost.value = { line, startMs: startMsAt(e, e.currentTarget, line, props.dragPayload) }
}

// Moving an existing entry: same DnD flow as a panel track, but the payload
// carries the entry id (excluded from overlap checks) and the grab offset.
function onEntryDragStart(e, entry) {
  const rect = e.currentTarget.getBoundingClientRect()
  const payload = {
    entryId: entry.id,
    trackId: entry.trackId,
    title: entry.title,
    artist: entry.artist,
    effDurationSec: entry.durationSec,
    override: entry.override,
    cueInSec: entry.cueInSec,
    cueOutSec: entry.cueOutSec,
    crossfadeSec: entry.crossfadeSec,
    grabOffsetMin: (e.clientX - rect.left) / props.zoom,
  }
  e.dataTransfer.effectAllowed = 'move'
  e.dataTransfer.setData('application/x-lc-entry', JSON.stringify(payload))
  emit('drag-start', payload)
}

function onDragLeave(e) {
  // Only clear when actually leaving the lane (not entering a child element).
  if (!e.currentTarget.contains(e.relatedTarget)) ghost.value = null
}

const ghostInfo = computed(() => {
  if (!ghost.value || !props.dragPayload) return null
  const startMs = ghost.value.startMs
  const endMs = startMs + (props.dragPayload.effDurationSec || 0) * 1000
  const { sameLine, needsOverride } = classifyOverlap(props.entries, {
    id: props.dragPayload.entryId ?? null, line: ghost.value.line, startMs, endMs,
  })
  // An entry that already has the override flag may overlap other lines freely.
  return { startMs, endMs, sameLine, needsOverride: needsOverride && !props.dragPayload.override }
})

const ghostStyle = computed(() => {
  if (!ghost.value || !props.dragPayload) return {}
  const left = xOf(ghost.value.startMs)
  const width = Math.max(6, ((props.dragPayload.effDurationSec || 0) / 60) * props.zoom)
  return { left: `${left}px`, width: `${width}px` }
})

function onDrop(e, line) {
  ghost.value = null
  const entryRaw = e.dataTransfer.getData('application/x-lc-entry')
  if (entryRaw) {
    const payload = JSON.parse(entryRaw)
    emit('move-entry', { payload, line, start: new Date(startMsAt(e, e.currentTarget, line, payload)) })
    return
  }
  const raw = e.dataTransfer.getData('application/x-lc-track')
  if (!raw) return
  const payload = JSON.parse(raw)
  emit('drop-track', { payload, line, start: new Date(startMsAt(e, e.currentTarget, line, payload)) })
}

watch(() => props.dragPayload, (v) => { if (!v) ghost.value = null })

// --- now line ---------------------------------------------------------------
const nowTick = ref(Date.now())
let timer = null
onMounted(() => { timer = setInterval(() => { nowTick.value = Date.now() }, 30000) })
onUnmounted(() => clearInterval(timer))

const nowX = computed(() => {
  const x = xOf(nowTick.value)
  return x >= 0 && x <= canvasW.value ? x : null
})

// Scroll today's view to the current time, other days to the start.
const scroller = ref(null)
watch([() => props.day, scroller], () => {
  if (!scroller.value) return
  scroller.value.scrollLeft = nowX.value != null ? Math.max(0, nowX.value - 200) : 0
}, { immediate: true })

function entryTooltip(e) {
  const ovr = e.override ? ` · ${t('schedule.override')}` : ''
  return `${e.artist ? e.artist + ' - ' : ''}${e.title}\n${hhmm(e.startUtc)} – ${hhmm(e.endUtc)} · ${lineLabel(e.line)}${ovr}`
}
</script>

<template>
  <div class="tg">
    <div class="labels">
      <div class="ruler-spacer" :style="{ height: RULER_H + 'px' }" />
      <div v-for="l in lanes" :key="l.line" class="lane-label" :style="{ height: LANE_H + 'px' }"
        :class="`lc${l.line}`" @click="startEdit(l.line)">
        <span class="dot" />
        <input v-if="editingLine === l.line" v-model="editName" class="lane-edit" maxlength="40"
          :ref="setEditEl" @click.stop
          @keydown.enter="commitEdit" @keydown.esc="cancelEdit" @blur="commitEdit" />
        <span v-else class="lane-name" :title="t('schedule.renameLine')">{{ l.label }}</span>
      </div>
    </div>
    <div ref="scroller" class="scroller">
      <div class="canvas" :style="{ width: canvasW + 'px' }">
        <div class="ruler" :style="{ height: RULER_H + 'px' }">
          <div v-for="h in 24" :key="h" class="hour" :style="{ width: hourW + 'px' }">{{ hh(h - 1) }}</div>
        </div>
        <div v-for="l in lanes" :key="l.line" class="lane"
          :style="{ height: LANE_H + 'px', backgroundSize: hourW + 'px 100%' }"
          @dragover.prevent="onDragOver($event, l.line)"
          @dragleave="onDragLeave"
          @drop.prevent="onDrop($event, l.line)">
          <div v-for="e in laneEntries(l.line)" :key="e.id" class="entry"
            :class="[`c${l.line}`, { ovr: e.override, dragging: dragPayload?.entryId === e.id }]"
            :style="entryStyle(e)" :title="entryTooltip(e)" draggable="true"
            @dragstart="onEntryDragStart($event, e)" @dragend="emit('drag-end')"
            @click="emit('edit', e)">
            <i v-if="e.override" class="pi pi-bolt" />
            <span class="e-title">{{ e.title }}</span>
            <span class="e-time">{{ hhmm(e.startUtc) }}</span>
          </div>
          <div v-if="ghost && ghost.line === l.line && ghostInfo" class="ghost"
            :class="{ bad: ghostInfo.sameLine, warn: !ghostInfo.sameLine && ghostInfo.needsOverride }"
            :style="ghostStyle" />
        </div>
        <div v-if="nowX != null" class="nowline" :style="{ left: nowX + 'px', top: RULER_H + 'px' }" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.tg { display: flex; background: var(--surface); border: 1px solid var(--border); border-radius: 12px; overflow: hidden; }
.labels { flex: 0 0 130px; border-right: 1px solid var(--border); background: var(--surface-2); }
.ruler-spacer { border-bottom: 1px solid var(--border); }
.lane-label { display: flex; align-items: center; gap: .5rem; padding: 0 .8rem; font-size: .85rem; font-weight: 600; border-bottom: 1px solid var(--border); cursor: text; }
.lane-label:last-child { border-bottom: none; }
.lane-label .dot { width: 9px; height: 9px; border-radius: 50%; flex: 0 0 auto; }
.lane-name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.lane-label:hover .lane-name { text-decoration: underline dotted; text-underline-offset: 3px; }
.lane-edit { min-width: 0; flex: 1; font: inherit; color: inherit; background: var(--surface);
  border: 1px solid var(--accent); border-radius: 4px; padding: .1rem .3rem; outline: none; }

.scroller { flex: 1; overflow-x: auto; }
.canvas { position: relative; }
.ruler { display: flex; border-bottom: 1px solid var(--border); font-size: .72rem; color: var(--text-muted); }
.hour { flex: 0 0 auto; border-right: 1px solid var(--border); padding: .3rem 0 0 .3rem; box-sizing: border-box; white-space: nowrap; overflow: hidden; }
.lane { position: relative; border-bottom: 1px solid var(--border); background-image: linear-gradient(90deg, var(--border) 0 1px, transparent 1px); background-repeat: repeat-x; }
.lane:last-of-type { border-bottom: none; }

.entry { position: absolute; top: 7px; bottom: 7px; border-radius: 6px; padding: .25rem .4rem; cursor: pointer;
  display: flex; flex-direction: column; justify-content: center; gap: .05rem; overflow: hidden;
  border: 1px solid transparent; font-size: .75rem; line-height: 1.15; }
.entry:hover { filter: brightness(1.15); }
.entry.dragging { opacity: .35; }
.e-title { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-weight: 600; }
.e-time { font-size: .68rem; opacity: .75; }
.entry .pi-bolt { position: absolute; top: 3px; right: 4px; font-size: .65rem; }
.entry.ovr { border-style: dashed; }

/* per-line colors: fallback gray, P1 strongest → P4 muted */
.c0 { background: rgba(148, 163, 184, .22); border-color: rgba(148, 163, 184, .55); }
.c1 { background: rgba(96, 165, 250, .28); border-color: rgba(96, 165, 250, .65); }
.c2 { background: rgba(52, 211, 153, .25); border-color: rgba(52, 211, 153, .6); }
.c3 { background: rgba(251, 191, 36, .22); border-color: rgba(251, 191, 36, .55); }
.c4 { background: rgba(192, 132, 252, .22); border-color: rgba(192, 132, 252, .55); }
.lc0 .dot { background: rgb(148, 163, 184); }
.lc1 .dot { background: rgb(96, 165, 250); }
.lc2 .dot { background: rgb(52, 211, 153); }
.lc3 .dot { background: rgb(251, 191, 36); }
.lc4 .dot { background: rgb(192, 132, 252); }

.ghost { position: absolute; top: 7px; bottom: 7px; border-radius: 6px; pointer-events: none;
  background: rgba(96, 165, 250, .35); border: 1px dashed rgba(96, 165, 250, .9); }
.ghost.warn { background: rgba(251, 191, 36, .3); border-color: rgba(251, 191, 36, .9); }
.ghost.bad { background: rgba(239, 68, 68, .3); border-color: rgba(239, 68, 68, .9); }

.nowline { position: absolute; bottom: 0; width: 2px; background: #ef4444; z-index: 2; pointer-events: none; }
</style>
