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

async function loadListeners() {
  try {
    listeners.value = (await api.get('/stats/listeners',
      { params: { range: listenerRange.value, tzOffset } })).data
  } catch { listeners.value = null }
}
async function loadPlays() {
  try {
    plays.value = (await api.get('/stats/plays',
      { params: { range: playRange.value, tzOffset } })).data
  } catch { plays.value = null }
}

watch(listenerRange, loadListeners)
watch(playRange, loadPlays)
onMounted(() => { loadListeners(); loadPlays() })

// --- tiles -------------------------------------------------------------------
const fmtTime = (v) => (v ? new Date(v).toLocaleString([], {
  month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '')

// Compact airtime for the tile ("142h 20m"); tables use fmtDuration.
function fmtAirtime(sec) {
  const min = Math.round((sec || 0) / 60)
  const h = Math.floor(min / 60)
  return h > 0 ? `${h}h ${min % 60}m` : `${min}m`
}

const tiles = computed(() => [
  { label: t('stats.peakListeners'), value: listeners.value?.peak ?? '—',
    sub: listeners.value?.peakUtc ? fmtTime(listeners.value.peakUtc) : '' },
  { label: t('stats.avgListeners'), value: listeners.value?.avg ?? '—' },
  { label: t('stats.totalPlays'), value: plays.value?.totalPlays ?? '—' },
  { label: t('stats.totalAirtime'),
    value: plays.value ? fmtAirtime(plays.value.totalAirtimeSec) : '—' },
  { label: t('stats.distinctTracks'), value: plays.value?.distinctTracks ?? '—' },
])

// --- chart data --------------------------------------------------------------
const hourPoints = computed(() => (listeners.value?.hourProfile || []).map((p) => ({
  label: String(p.hour), value: p.avg, secondary: p.peak,
})))

// Weekday 0=Sun..6=Sat from the API; render Mon-first with localized short names.
// 2024-01-01 is a Monday.
const weekdayName = (d) => new Date(Date.UTC(2024, 0, 1 + ((d + 6) % 7)))
  .toLocaleDateString(locale.value, { weekday: 'short', timeZone: 'UTC' })
const weekdayPoints = computed(() => {
  const prof = listeners.value?.weekdayProfile || []
  return [1, 2, 3, 4, 5, 6, 0].map((d) => {
    const p = prof.find((x) => x.weekday === d)
    return { label: weekdayName(d), value: p?.avg ?? 0, secondary: p?.peak ?? 0 }
  })
})

const perDayPoints = computed(() => (plays.value?.perDay || []).map((p) => ({
  label: new Date(p.date).toLocaleDateString([], { month: 'short', day: 'numeric' }),
  value: p.count,
})))

const trackName = (row) => (row.artist ? `${row.artist} - ${row.title || '?'}` : (row.title || '?'))
</script>

<template>
  <div class="page">
    <h1>{{ t('stats.title') }}</h1>

    <div class="tiles">
      <div v-for="tile in tiles" :key="tile.label" class="tile">
        <div class="tile-label">{{ tile.label }}</div>
        <div class="tile-value">{{ tile.value }}</div>
        <div v-if="tile.sub" class="tile-sub muted">{{ tile.sub }}</div>
      </div>
    </div>

    <div class="card">
      <div class="section-head">
        <h2>{{ t('stats.listeners') }}</h2>
        <Select v-model="listenerRange" :options="listenerRanges"
          optionLabel="label" optionValue="value" size="small" />
      </div>
      <div class="charts2">
        <StatsBarChart :title="t('stats.byHour')" :points="hourPoints"
          :valueLabel="t('stats.avg')" :secondaryLabel="t('stats.peak')" :emptyText="t('stats.noData')" />
        <StatsBarChart :title="t('stats.byWeekday')" :points="weekdayPoints"
          :valueLabel="t('stats.avg')" :secondaryLabel="t('stats.peak')" :emptyText="t('stats.noData')" />
      </div>
    </div>

    <div class="card">
      <div class="section-head">
        <h2>{{ t('stats.plays') }}</h2>
        <Select v-model="playRange" :options="playRanges"
          optionLabel="label" optionValue="value" size="small" />
      </div>
      <StatsBarChart :title="t('stats.playsPerDay')" :points="perDayPoints"
        :valueLabel="t('stats.playsWord')" :emptyText="t('stats.noData')" />
      <div class="tables2">
        <div>
          <h3 class="card-title">{{ t('stats.topTracks') }}</h3>
          <DataTable :value="plays?.topTracks || []" size="small" stripedRows>
            <Column :header="t('stats.track')">
              <template #body="{ data }">{{ trackName(data) }}</template>
            </Column>
            <Column field="plays" :header="t('stats.playsWord')" style="width:5.5rem" class="num" />
            <Column :header="t('stats.airtime')" style="width:7rem" class="num">
              <template #body="{ data }">{{ fmtDuration(data.airtimeSec) }}</template>
            </Column>
          </DataTable>
        </div>
        <div>
          <h3 class="card-title">{{ t('stats.topArtists') }}</h3>
          <DataTable :value="plays?.topArtists || []" size="small" stripedRows>
            <Column field="artist" :header="t('stats.artist')" />
            <Column field="plays" :header="t('stats.playsWord')" style="width:5.5rem" class="num" />
            <Column :header="t('stats.airtime')" style="width:7rem" class="num">
              <template #body="{ data }">{{ fmtDuration(data.airtimeSec) }}</template>
            </Column>
          </DataTable>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr)); gap: 1rem; margin-bottom: 1rem; }
.tile { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: .85rem 1rem; }
.tile-label { font-size: .8rem; color: var(--text-muted); }
.tile-value { font-size: 1.6rem; font-weight: 600; color: var(--text-strong); margin-top: .15rem; }
.tile-sub { font-size: .75rem; margin-top: .1rem; }

.card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 1rem 1.1rem; margin-bottom: 1rem; }
.section-head { display: flex; align-items: center; justify-content: space-between; gap: .75rem; margin-bottom: .75rem; }
.section-head h2 { margin: 0; font-size: 1.05rem; }
.card-title { margin: 0 0 .5rem; font-size: .95rem; font-weight: 600; }

.charts2 { display: grid; grid-template-columns: 1fr 1fr; gap: 1.25rem; }
.tables2 { display: grid; grid-template-columns: 1fr 1fr; gap: 1.25rem; margin-top: 1.25rem; }
.num :deep(td), .num :deep(th) { font-variant-numeric: tabular-nums; }

@media (max-width: 900px) { .charts2, .tables2 { grid-template-columns: 1fr; } }
</style>
