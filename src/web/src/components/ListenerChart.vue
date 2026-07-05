<script setup>
import { ref, shallowRef, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'
import Select from 'primevue/select'
import {
  Chart, LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip,
} from 'chart.js'

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip)

const { t } = useI18n()

const range = ref('24h')
const ranges = computed(() => [
  { label: t('monitor.range24h'), value: '24h' },
  { label: t('monitor.rangeWeek'), value: 'week' },
  { label: t('monitor.rangeMonth'), value: 'month' },
])

const canvas = ref(null)
const chart = shallowRef(null)
const hasData = ref(true)
let points = []

// Read a CSS custom property off the document root (the app is single-theme dark).
function cssVar(name) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim()
}

// x-axis label for a bucket timestamp — time-of-day for 24h, date otherwise.
function labelFor(iso) {
  const d = new Date(iso)
  if (range.value === '24h') return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  return d.toLocaleDateString([], { month: 'short', day: 'numeric' })
}
function tooltipTitle(iso) {
  const d = new Date(iso)
  return range.value === '24h'
    ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}

async function load() {
  try {
    const { data } = await api.get('/stream/listeners', { params: { range: range.value } })
    points = data.points || []
  } catch {
    points = []
  }
  hasData.value = points.some((p) => p.avg > 0 || p.peak > 0)
  await nextTick()
  render()
}

function render() {
  if (!canvas.value) return
  const accent = cssVar('--accent') || '#5f86c9'
  const border = cssVar('--border') || '#353c49'
  const textMuted = cssVar('--text-muted') || '#9aa3b4'

  const ctx = canvas.value.getContext('2d')
  const grad = ctx.createLinearGradient(0, 0, 0, canvas.value.height || 180)
  grad.addColorStop(0, hexA(accent, 0.35))
  grad.addColorStop(1, hexA(accent, 0))

  const labels = points.map((p) => labelFor(p.t))
  const data = points.map((p) => p.avg)
  const peaks = points.map((p) => p.peak)
  const isos = points.map((p) => p.t)

  if (chart.value) chart.value.destroy()
  chart.value = new Chart(ctx, {
    type: 'line',
    data: {
      labels,
      datasets: [{
        data,
        borderColor: accent,
        borderWidth: 2,
        backgroundColor: grad,
        fill: true,
        tension: 0.35,
        pointRadius: 0,
        pointHoverRadius: 4,
        pointHoverBackgroundColor: accent,
      }],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      interaction: { mode: 'index', intersect: false },
      scales: {
        x: {
          grid: { display: false },
          ticks: { color: textMuted, maxTicksLimit: 6, maxRotation: 0, autoSkip: true },
          border: { color: border },
        },
        y: {
          beginAtZero: true,
          grid: { color: border },
          border: { display: false },
          ticks: { color: textMuted, precision: 0, maxTicksLimit: 5 },
        },
      },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            title: (items) => tooltipTitle(isos[items[0].dataIndex]),
            label: (item) => {
              const i = item.dataIndex
              return `${t('monitor.avg')}: ${data[i]}   ${t('monitor.peak')}: ${peaks[i]}`
            },
          },
        },
      },
    },
  })
}

// Turn "#rrggbb" into an rgba() with the given alpha (chart.js gradient stops).
function hexA(hex, alpha) {
  const m = hex.replace('#', '')
  const full = m.length === 3 ? m.split('').map((c) => c + c).join('') : m
  const n = parseInt(full, 16)
  const r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

watch(range, load)
onMounted(load)
onUnmounted(() => { if (chart.value) chart.value.destroy() })
</script>

<template>
  <div class="lc">
    <div class="lc-head">
      <h3 class="card-title" style="margin:0">{{ t('monitor.listenerHistory') }}</h3>
      <Select v-model="range" :options="ranges" optionLabel="label" optionValue="value" size="small" />
    </div>
    <div class="lc-body">
      <canvas ref="canvas" />
      <div v-if="!hasData" class="lc-empty muted">{{ t('monitor.noData') }}</div>
    </div>
  </div>
</template>

<style scoped>
.lc { margin-top: 1.25rem; padding-top: 1.25rem; border-top: 1px solid var(--border); }
.lc-head { display: flex; align-items: center; justify-content: space-between; gap: .75rem; margin-bottom: .5rem; }
.lc-body { position: relative; height: 180px; }
.lc-empty {
  position: absolute; inset: 0; display: flex; align-items: center; justify-content: center;
  font-size: .85rem; pointer-events: none;
}
</style>
