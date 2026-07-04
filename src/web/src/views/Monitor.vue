<script setup>
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import * as signalR from '@microsoft/signalr'
import { api } from '../api/client'
import { fmtDuration } from '../util'
import Button from 'primevue/button'
import ToggleSwitch from 'primevue/toggleswitch'
import Tag from 'primevue/tag'
import Timeline from 'primevue/timeline'
import { useToast } from 'primevue/usetoast'

const { t } = useI18n()
const toast = useToast()
const snap = ref(null)
const settings = ref(null)
const elapsed = ref(0)
let conn = null
let timer = null

const onAir = computed(() => snap.value?.onAir || (snap.value?.currentTitle
  ? `${snap.value.currentArtist ? snap.value.currentArtist + ' - ' : ''}${snap.value.currentTitle}`
  : null))

const fmtTime = (v) => (v ? new Date(v).toLocaleTimeString() : '')

// Split title/artist when the snapshot has them; otherwise fall back to the combined string.
const nowTitle = computed(() => snap.value?.currentTitle || onAir.value)
const nowArtist = computed(() => (snap.value?.currentTitle && snap.value?.currentArtist) || '')
const remaining = computed(() => Math.max(0, (snap.value?.currentDurationSec || 0) - elapsed.value))

// Previous / Now / Up next — three rows for the timeline, each with its start time.
const events = computed(() => {
  const s = snap.value
  const start = s?.currentStartedUtc ? new Date(s.currentStartedUtc).getTime() : null
  const nextTs = start ? start + (s?.currentDurationSec || 0) * 1000 : null
  return [
    { role: t('monitor.prev'), label: s?.previous || '—', time: s?.previous ? fmtTime(s?.previousStartedUtc) : '',
      icon: 'pi-step-backward', current: false },
    { role: t('monitor.now'), label: onAir.value || t('monitor.silence'), time: fmtTime(s?.currentStartedUtc),
      icon: 'pi-volume-up', current: true },
    { role: t('monitor.upNext'), label: s?.upNext?.[0] || '—', time: s?.upNext?.[0] ? fmtTime(nextTs) : '',
      icon: 'pi-step-forward', current: false },
  ]
})

const progress = computed(() => {
  const d = snap.value?.currentDurationSec || 0
  return d > 0 ? Math.min(100, (elapsed.value / d) * 100) : 0
})

function recomputeElapsed() {
  if (snap.value?.currentStartedUtc) {
    elapsed.value = (Date.now() - new Date(snap.value.currentStartedUtc).getTime()) / 1000
  } else {
    elapsed.value = 0
  }
}

async function start() {
  conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/monitor')
    .withAutomaticReconnect()
    .build()
  conn.on('snapshot', (s) => { snap.value = s; recomputeElapsed() })
  try { await conn.start() } catch { /* fall back to polling below */ }
  timer = setInterval(async () => {
    recomputeElapsed()
    if (!conn || conn.state !== 'Connected') {
      try { snap.value = (await api.get('/stream/status')).data; recomputeElapsed() } catch { /* ignore */ }
    }
  }, 1000)
}

onMounted(async () => {
  start()
  try { settings.value = (await api.get('/settings')).data } catch { /* ignore */ }
})
onUnmounted(() => { if (timer) clearInterval(timer); conn?.stop() })

async function skip() { await api.post('/stream/skip') }
async function restart() { await api.post('/stream/restart') }
async function toggleScheduler(v) { await api.post('/stream/scheduler', { enabled: v }) }
const schedulerEnabled = computed({
  get: () => snap.value?.schedulerEnabled ?? true,
  set: (v) => { if (snap.value) snap.value.schedulerEnabled = v; toggleScheduler(v) },
})

const streamUrl = computed(() => {
  if (!snap.value) return ''
  const override = settings.value?.publicStreamUrl?.trim()
  return override || `http://${location.hostname}:8000/stream`
})

// Listen: minimal custom player (play/pause + volume). No seek bar — a livestream has no position.
const audioEl = ref(null)
const playing = ref(false)
const volume = ref(1)
function togglePlay() {
  const el = audioEl.value
  if (!el) return
  if (el.paused) el.play().catch(() => {})   // play() rejects if autoplay blocked / not ready
  else el.pause()
}
watch(volume, (v) => { if (audioEl.value) audioEl.value.volume = v })

const lastVol = ref(1)
function toggleMute() {
  if (volume.value > 0) { lastVol.value = volume.value; volume.value = 0 }
  else volume.value = lastVol.value || 1
}
const volIcon = computed(() =>
  volume.value === 0 ? 'pi-volume-off' : volume.value < 0.5 ? 'pi-volume-down' : 'pi-volume-up')

async function copyUrl() {
  try {
    await navigator.clipboard.writeText(streamUrl.value)
    toast.add({ severity: 'success', summary: t('monitor.copied'), life: 2000 })
  } catch { /* clipboard unavailable (http origin) — URL is still selectable */ }
}
</script>

<template>
  <div class="page">
    <h1>{{ t('monitor.title') }}</h1>

    <div class="grid">
      <div class="card big">
        <div class="onair">
          <div class="onair-top">
            <h3 class="card-title" style="margin:0">{{ t('monitor.nowOnAir') }}</h3>
            <span v-if="snap?.fallbackActive" class="badge warn">
              <span class="dot" /> {{ t('monitor.fallbackActive') }}</span>
            <span v-else-if="onAir" class="badge live">
              <span class="dot" /> {{ t('monitor.live') }}</span>
          </div>
          <div class="onair-main">
            <div class="eq" :class="{ paused: !onAir }"><span /><span /><span /><span /></div>
            <div class="onair-text">
              <div class="onair-title" :class="{ muted: !onAir }">{{ nowTitle || t('monitor.silence') }}</div>
              <div v-if="nowArtist" class="onair-artist">{{ nowArtist }}</div>
            </div>
            <span v-if="snap?.currentPlaylistName" class="chip">
              <i class="pi pi-list" /> {{ snap.currentPlaylistName }}</span>
          </div>
          <div class="bar"><div class="fill" :style="{ width: progress + '%' }" /></div>
          <div class="times muted">
            <span>{{ fmtDuration(elapsed) }}</span>
            <span v-if="snap?.currentDurationSec">-{{ fmtDuration(remaining) }} · {{ fmtDuration(snap.currentDurationSec) }}</span>
            <span v-else>--:--</span>
          </div>
        </div>

        <div class="controls">
          <Button :label="t('monitor.skip')" icon="pi pi-step-forward" size="small" @click="skip" />
          <Button :label="t('monitor.restartLs')" icon="pi pi-refresh" size="small" severity="secondary" @click="restart" />
          <div class="row" style="margin-left:auto">
            <span class="muted">{{ t('monitor.scheduler') }}</span>
            <ToggleSwitch v-model="schedulerEnabled" />
          </div>
        </div>

        <div class="listen">
          <h3 class="card-title">{{ t('monitor.listen') }}</h3>
          <div v-if="streamUrl" class="listen-player">
            <audio ref="audioEl" :src="streamUrl" preload="none"
                   @play="playing = true" @pause="playing = false" @ended="playing = false" />
            <span class="play-wrap" :class="{ playing }">
              <Button :icon="playing ? 'pi pi-pause' : 'pi pi-play'" rounded @click="togglePlay" />
            </span>
            <div class="eq eq-sm" :class="{ paused: !playing }"><span /><span /><span /><span /></div>
            <Button :icon="'pi ' + volIcon" text rounded size="small" @click="toggleMute" />
            <input type="range" min="0" max="1" step="0.05" v-model.number="volume" class="vol"
                   :style="{ '--vol': (volume * 100) + '%' }" />
            <span class="vol-pct muted">{{ Math.round(volume * 100) }}%</span>
          </div>
          <div class="url-row mt">
            <code class="url">{{ streamUrl }}</code>
            <Button icon="pi pi-copy" text size="small" @click="copyUrl" />
          </div>
        </div>
      </div>

      <div class="card">
        <h3 class="card-title">{{ t('monitor.status') }}</h3>
        <div class="stat"><span>{{ t('monitor.liquidsoap') }}</span>
          <Tag :severity="snap?.liquidsoapUp ? 'success' : 'danger'" :value="snap?.liquidsoapUp ? t('monitor.up') : t('monitor.down')" /></div>
        <div class="stat"><span>{{ t('monitor.icecast') }}</span>
          <Tag :severity="snap?.icecastConnected ? 'success' : 'danger'" :value="snap?.icecastConnected ? t('monitor.connected') : t('monitor.offline')" /></div>
        <div class="stat"><span>{{ t('monitor.listeners') }}</span><b>{{ snap?.listeners ?? 0 }}</b></div>
        <div class="stat"><span>{{ t('monitor.playlist') }}</span><b>{{ snap?.currentPlaylistName || '—' }}</b></div>
      </div>

      <div class="card">
        <h3 class="card-title">{{ t('monitor.scheduled') }}</h3>
        <Timeline :value="events" class="tl mt">
          <template #opposite="{ item }">
            <span class="tl-role" :class="{ cur: item.current }">{{ item.role }}</span>
            <span v-if="item.time" class="tl-time">{{ item.time }}</span>
          </template>
          <template #marker="{ item }">
            <span class="tl-dot" :class="{ cur: item.current }"><i :class="['pi', item.icon]" /></span>
          </template>
          <template #content="{ item }">
            <span class="tl-track" :class="{ cur: item.current }">{{ item.label }}</span>
          </template>
        </Timeline>
      </div>
    </div>
  </div>
</template>

<style scoped>
.grid { display: grid; grid-template-columns: 2fr 1fr; gap: 1rem; }
.card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 1rem 1.1rem; }
.big { grid-row: span 2; display: flex; flex-direction: column; }
.listen { margin-top: auto; padding-top: 1.25rem; border-top: 1px solid var(--border); }
.card-title { margin: 0 0 .75rem; font-size: 1rem; font-weight: 600; }

/* --- Now on air hero --- */
.onair { border: 1px solid var(--border); border-radius: 12px; padding: 1rem 1.1rem .85rem;
  background: radial-gradient(120% 160% at 0% 0%, var(--accent-soft) 0%, transparent 55%), var(--surface-2); }
.onair-top { display: flex; align-items: center; justify-content: space-between; gap: .75rem; }
.onair-main { display: flex; align-items: center; gap: .9rem; margin: .9rem 0 1.1rem; min-height: 3rem; }
.onair-text { min-width: 0; flex: 1; }
.onair-title { font-size: 1.45rem; font-weight: 700; color: var(--text-strong); line-height: 1.25;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.onair-artist { color: var(--text-muted); font-size: .95rem; margin-top: .15rem;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.chip { display: inline-flex; align-items: center; gap: .4rem; font-size: .78rem; color: var(--accent-contrast);
  background: var(--accent-soft); border: 1px solid var(--accent); border-radius: 999px;
  padding: .25rem .7rem; white-space: nowrap; }

.badge { display: inline-flex; align-items: center; gap: .45rem; font-size: .7rem; font-weight: 700;
  letter-spacing: 1px; text-transform: uppercase; border-radius: 999px; padding: .22rem .65rem; }
.badge .dot { width: 7px; height: 7px; border-radius: 50%; background: currentColor; animation: pulse 1.6s infinite; }
.badge.live { color: #ff6b6b; background: rgba(255,107,107,.08); border: 1px solid rgba(255,107,107,.35); }
.badge.warn { color: var(--warn); background: rgba(224,177,92,.08); border: 1px solid rgba(224,177,92,.35); }
@keyframes pulse {
  0% { box-shadow: 0 0 0 0 color-mix(in srgb, currentColor 50%, transparent); }
  70% { box-shadow: 0 0 0 6px transparent; }
  100% { box-shadow: 0 0 0 0 transparent; }
}

/* Animated equalizer bars */
.eq { display: flex; align-items: flex-end; gap: 3px; width: 26px; height: 26px; flex: none; }
.eq span { flex: 1; border-radius: 2px; background: linear-gradient(180deg, var(--accent-2), var(--accent));
  animation: eq 1.1s ease-in-out infinite; }
.eq span:nth-child(1) { animation-delay: 0s; }
.eq span:nth-child(2) { animation-delay: .28s; }
.eq span:nth-child(3) { animation-delay: .12s; }
.eq span:nth-child(4) { animation-delay: .4s; }
.eq.paused span { animation: none; height: 28%; opacity: .45; }
.eq-sm { width: 18px; height: 18px; }
@keyframes eq { 0%, 100% { height: 30%; } 50% { height: 100%; } }

.bar { height: 8px; background: var(--border); border-radius: 6px; overflow: hidden; }
.fill { height: 100%; background: linear-gradient(90deg, var(--accent), var(--accent-2));
  box-shadow: 0 0 10px color-mix(in srgb, var(--accent) 60%, transparent); transition: width .9s linear; }
.times { display: flex; justify-content: space-between; margin-top: .35rem; font-size: .85rem;
  font-variant-numeric: tabular-nums; }

/* --- Listen --- */
.listen-player { display: flex; align-items: center; gap: .7rem; margin-top: .5rem; }
.play-wrap { position: relative; display: inline-flex; border-radius: 50%; flex: none; }
.play-wrap.playing::after { content: ''; position: absolute; inset: 0; border-radius: 50%;
  animation: pulse 1.6s infinite; color: var(--accent); pointer-events: none; }
.vol { flex: 1; -webkit-appearance: none; appearance: none; height: 6px; border-radius: 6px; outline: none;
  background: linear-gradient(to right, var(--accent) var(--vol, 100%), var(--border) var(--vol, 100%)); }
.vol::-webkit-slider-thumb { -webkit-appearance: none; appearance: none; width: 14px; height: 14px;
  border-radius: 50%; background: var(--text-strong); border: 2px solid var(--accent); cursor: pointer; }
.vol::-moz-range-thumb { width: 14px; height: 14px; border-radius: 50%;
  background: var(--text-strong); border: 2px solid var(--accent); cursor: pointer; }
.vol-pct { font-size: .8rem; width: 2.6rem; text-align: right; font-variant-numeric: tabular-nums; }
.url-row { display: flex; align-items: center; gap: .25rem; }
.url { font-size: .78rem; color: var(--text-muted); background: var(--surface-2);
  border: 1px solid var(--border); border-radius: 6px; padding: .3rem .55rem;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; }
.controls { display: flex; gap: .5rem; align-items: center; margin-top: 1.25rem; }
.stat { display: flex; justify-content: space-between; align-items: center; padding: .45rem 0; border-bottom: 1px solid var(--border); }
.stat:last-child { border-bottom: none; }
.tl { padding-top: .25rem; }
.tl :deep(.p-timeline-event-opposite) { flex: 0 0 5rem; padding: 0 .75rem 1.2rem 0; }
.tl :deep(.p-timeline-event-content) { padding: 0 0 1.2rem .75rem; }
.tl :deep(.p-timeline-event-connector) { background: var(--border); }
.tl-role { display: block; font-size: .75rem; color: var(--text-muted); text-transform: uppercase; letter-spacing: .4px; }
.tl-role.cur { color: var(--accent); }
.tl-time { display: block; font-size: .72rem; color: var(--text-dim); margin-top: .15rem; font-variant-numeric: tabular-nums; }
.tl-track { color: var(--text-muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; display: block; }
.tl-track.cur { color: var(--text-strong); font-weight: 600; }
.tl-dot { display: grid; place-items: center; width: 1.9rem; height: 1.9rem; border-radius: 50%;
  background: var(--surface-3); border: 1px solid var(--border); color: var(--text-muted); font-size: .8rem; }
.tl-dot.cur { background: var(--accent-soft); border-color: var(--accent); color: var(--accent-contrast); }
@media (max-width: 800px) { .grid { grid-template-columns: 1fr; } .big { grid-row: auto; } }
</style>
