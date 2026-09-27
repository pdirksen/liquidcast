<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'
import { fmtDuration } from '../util'
import Select from 'primevue/select'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import StatsBarChart from '../components/StatsBarChart.vue'

const { t, locale } = useI18n()

const tzOffset = new Date().getTimezoneOffset()

const listenerRange = ref('month')
const listenerRanges = computed(() => [
  { label: t('stats.range7d'), value: 'week' },
  { label: t('stats.range30d'), value: 'month' },
])
const playRange = ref('month')
const playRanges = computed(() => [
  { label: t('stats.range7d'), value: 'week' },
  { label: t('stats.range30d'), value: 'month' },
  { label: t('stats.range1y'), value: 'year' },
])

const listeners = ref(null)
const plays = ref(null)
const loadingListeners = ref(false)
const loadingPlays = ref(false)

// Sequence guards: a slow response for an old range must not overwrite a newer one.
let listenerSeq = 0
let playSeq = 0
async function loadListeners() {
  const seq = ++listenerSeq
  loadingListeners.value = true
  try {
    const data = (await api.get('/stats/listeners',
      { params: { range: listenerRange.value, tzOffset } })).data
    if (seq === listenerSeq) listeners.value = data
  } catch { if (seq === listenerSeq) listeners.value = null }
  finally { if (seq === listenerSeq) loadingListeners.value = false }
}
async function loadPlays() {
  const seq = ++playSeq
  loadingPlays.value = true
  try {
    const data = (await api.get('/stats/plays',
      { params: { range: playRange.value, tzOffset } })).data
    if (seq === playSeq) plays.value = data
  } catch { if (seq === playSeq) plays.value = null }
  finally { if (seq === playSeq) loadingPlays.value = false }
}

watch(listenerRange, loadListeners)
watch(playRange, loadPlays)
onMounted(() => { loadListeners(); loadPlays() })

// --- tiles -------------------------------------------------------------------
const fmtTime = (v) => (v ? new Date(v).toLocaleString(locale.value, {
  weekday: 'short', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '')
const fmtNum = (v, digits = 0) => (v == null ? '—'
  : Number(v).toLocaleString(locale.value, { maximumFractionDigits: digits }))

// Compact airtime for the tile ("142h 20m"); tables use fmtDuration.
function fmtAirtime(sec) {
  const min = Math.round((sec || 0) / 60)
  const h = Math.floor(min / 60)
  return h > 0 ? `${h}h ${min % 60}m` : `${min}m`
}

const listenerTiles = computed(() => [
  { label: t('stats.peakListeners'), value: fmtNum(listeners.value?.peak),
    sub: listeners.value?.peak ? fmtTime(listeners.value.peakUtc) : '' },
  { label: t('stats.avgListeners'), value: fmtNum(listeners.value?.avg, 1) },
  { label: t('stats.listenerHours'), value: fmtNum(listeners.value?.listenerHours) },
])

const playTiles = computed(() => [
  { label: t('stats.totalPlays'), value: fmtNum(plays.value?.totalPlays) },
  { label: t('stats.totalAirtime'),
    value: plays.value ? fmtAirtime(plays.value.totalAirtimeSec) : '—' },
  { label: t('stats.distinctTracks'), value: fmtNum(plays.value?.distinctTracks) },
])

// --- chart data --------------------------------------------------------------
const pad2 = (n) => String(n).padStart(2, '0')
const hourPoints = computed(() => (listeners.value?.hourProfile || []).map((p) => ({
  label: pad2(p.hour),
  tooltipTitle: `${pad2(p.hour)}:00–${pad2((p.hour + 1) % 24)}:00`,
  value: p.avg, secondary: p.peak,
})))

// Weekday 0=Sun..6=Sat from the API; render Mon-first with localized names.
// 2024-01-01 is a Monday.
const weekdayName = (d, weekday) => new Date(Date.UTC(2024, 0, 1 + ((d + 6) % 7)))
  .toLocaleDateString(locale.value, { weekday, timeZone: 'UTC' })
const weekdayPoints = computed(() => {
  const prof = listeners.value?.weekdayProfile || []
  return [1, 2, 3, 4, 5, 6, 0].map((d) => {
    const p = prof.find((x) => x.weekday === d)
    return { label: weekdayName(d, 'short'), tooltipTitle: weekdayName(d, 'long'),
      value: p?.avg ?? 0, secondary: p?.peak ?? 0 }
  })
})

// Series dates are local calendar days/months without a zone ("2026-09-01T00:00:00"),
// which Date parses as local time — no shifting.
const byMonth = computed(() => plays.value?.bucket === 'month')
const seriesPoints = computed(() => (plays.value?.series || []).map((p) => {
  const d = new Date(p.date)
  return byMonth.value
    ? { label: d.toLocaleDateString(locale.value, { month: 'short' }),
        tooltipTitle: d.toLocaleDateString(locale.value, { month: 'long', year: 'numeric' }),
        value: p.count }
    : { label: d.toLocaleDateString(locale.value, { month: 'short', day: 'numeric' }),
        tooltipTitle: d.toLocaleDateString(locale.value, { weekday: 'short', month: 'long', day: 'numeric' }),
        value: p.count }
}))

// --- tables ------------------------------------------------------------------
const withShare = (rows) => {
  const max = Math.max(1, ...rows.map((r) => r.plays))
  return rows.map((r, i) => ({ ...r, rank: i + 1, share: (r.plays / max) * 100 }))
}
const topTracks = computed(() => withShare(plays.value?.topTracks || []))
const topArtists = computed(() => withShare(plays.value?.topArtists || []))
</script>

<template>
  <div class="page">
    <h1>{{ t('stats.title') }}</h1>

    <section class="card" :class="{ loading: loadingListeners }">
      <div class="section-head">
        <h2>{{ t('stats.listeners') }}</h2>
        <Select v-model="listenerRange" :options="listenerRanges"
          optionLabel="label" optionValue="value" size="small" />
      </div>
      <div class="tiles">
        <div v-for="tile in listenerTiles" :key="tile.label" class="tile">
          <div class="tile-label">{{ tile.label }}</div>
          <div class="tile-value">{{ tile.value }}</div>
          <div v-if="tile.sub" class="tile-sub muted">{{ tile.sub }}</div>
        </div>
      </div>
      <div class="charts2">
        <StatsBarChart :title="t('stats.byHour')" :points="hourPoints"
          :valueLabel="t('stats.avg')" :secondaryLabel="t('stats.peak')" :emptyText="t('stats.noData')" />
        <StatsBarChart :title="t('stats.byWeekday')" :points="weekdayPoints"
          :valueLabel="t('stats.avg')" :secondaryLabel="t('stats.peak')" :emptyText="t('stats.noData')" />
      </div>
    </section>

    <section class="card" :class="{ loading: loadingPlays }">
      <div class="section-head">
        <h2>{{ t('stats.plays') }}</h2>
        <Select v-model="playRange" :options="playRanges"
          optionLabel="label" optionValue="value" size="small" />
      </div>
      <div class="tiles">
        <div v-for="tile in playTiles" :key="tile.label" class="tile">
          <div class="tile-label">{{ tile.label }}</div>
          <div class="tile-value">{{ tile.value }}</div>
        </div>
      </div>
      <StatsBarChart :title="byMonth ? t('stats.playsPerMonth') : t('stats.playsPerDay')"
        :points="seriesPoints" :valueLabel="t('stats.playsWord')" :emptyText="t('stats.noData')" />
      <div class="tables2">
        <div>
          <h3 class="card-title">{{ t('stats.topTracks') }}</h3>
          <DataTable :value="topTracks" size="small" rowHover class="top">
            <Column field="rank" header="#" class="rank" />
            <Column :header="t('stats.track')">
              <template #body="{ data }">
                <div class="name" :title="data.title || '?'">{{ data.title || '?' }}</div>
                <div v-if="data.artist" class="sub muted" :title="data.artist">{{ data.artist }}</div>
              </template>
            </Column>
            <Column :header="t('stats.playsWord')" class="plays">
              <template #body="{ data }">
                <div class="share"><span class="share-bar" :style="{ width: data.share + '%' }" /></div>
                <span class="num">{{ fmtNum(data.plays) }}</span>
              </template>
            </Column>
            <Column :header="t('stats.airtime')" class="airtime">
              <template #body="{ data }"><span class="num muted">{{ fmtDuration(data.airtimeSec) }}</span></template>
            </Column>
            <template #empty><div class="empty muted">{{ t('stats.noData') }}</div></template>
          </DataTable>
        </div>
        <div>
          <h3 class="card-title">{{ t('stats.topArtists') }}</h3>
          <DataTable :value="topArtists" size="small" rowHover class="top">
            <Column field="rank" header="#" class="rank" />
            <Column :header="t('stats.artist')">
              <template #body="{ data }"><div class="name" :title="data.artist">{{ data.artist }}</div></template>
            </Column>
            <Column :header="t('stats.playsWord')" class="plays">
              <template #body="{ data }">
                <div class="share"><span class="share-bar" :style="{ width: data.share + '%' }" /></div>
                <span class="num">{{ fmtNum(data.plays) }}</span>
              </template>
            </Column>
            <Column :header="t('stats.airtime')" class="airtime">
              <template #body="{ data }"><span class="num muted">{{ fmtDuration(data.airtimeSec) }}</span></template>
            </Column>
            <template #empty><div class="empty muted">{{ t('stats.noData') }}</div></template>
          </DataTable>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 1rem 1.1rem; margin-bottom: 1rem;
  transition: opacity .15s; }
.card.loading { opacity: .6; }
.section-head { display: flex; align-items: center; justify-content: space-between; gap: .75rem; margin-bottom: .85rem; }
.section-head h2 { margin: 0; font-size: 1.05rem; }
.card-title { margin: 0 0 .5rem; font-size: .95rem; font-weight: 600; }

.tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(8.5rem, 1fr)); gap: .75rem; margin-bottom: 1.25rem; }
.tile { background: var(--surface-2); border: 1px solid var(--border); border-radius: 10px; padding: .75rem .9rem; }
.tile-label { font-size: .8rem; color: var(--text-muted); }
.tile-value { font-size: 1.6rem; font-weight: 600; color: var(--text-strong); margin-top: .15rem;
  font-variant-numeric: tabular-nums; }
.tile-sub { font-size: .75rem; margin-top: .1rem; }

.charts2 { display: grid; grid-template-columns: 1fr 1fr; gap: 1.25rem; }
.tables2 { display: grid; grid-template-columns: 1fr 1fr; gap: 1.25rem; margin-top: 1.5rem; }

/* Top lists: fixed layout so long names ellipsize instead of stretching the table. */
.top :deep(table) { table-layout: fixed; width: 100%; }
.top :deep(td) { vertical-align: middle; }
.top :deep(th.rank) { width: 2.5rem; }
.top :deep(th.plays) { width: 8.5rem; }
.top :deep(th.airtime) { width: 5.5rem; }
.top :deep(td.rank) { color: var(--text-dim); font-variant-numeric: tabular-nums; }
.top :deep(td.airtime) { text-align: right; }
.top :deep(th.airtime .p-datatable-column-header-content) { justify-content: flex-end; }
.name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text); }
.sub { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .8rem; }
.num { font-variant-numeric: tabular-nums; }
.share { display: inline-block; vertical-align: middle; width: 4.5rem; height: 6px; margin-right: .5rem;
  background: var(--border-strong); border-radius: 3px; overflow: hidden; }
.share-bar { display: block; height: 100%; background: var(--accent); border-radius: 3px; }
.empty { text-align: center; padding: 1rem 0; font-size: .85rem; }

@media (max-width: 900px) { .charts2, .tables2 { grid-template-columns: 1fr; } }
/* Phones: drop the share bars so the name column keeps its room. */
@media (max-width: 560px) {
  .share { display: none; }
  .top :deep(th.plays) { width: 3.5rem; }
  .top :deep(th.rank) { width: 2rem; }
  .tile-value { font-size: 1.35rem; }
}
</style>
