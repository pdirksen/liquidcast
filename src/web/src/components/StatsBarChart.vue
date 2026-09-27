<script setup>
import { ref, shallowRef, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { cssVar, hexA } from '../utils/chart'
import { Chart, BarController, BarElement, LinearScale, CategoryScale, Tooltip } from 'chart.js'

Chart.register(BarController, BarElement, LinearScale, CategoryScale, Tooltip)

const props = defineProps({
  title: { type: String, required: true },
  // [{ label, value, secondary?, tooltipTitle? }] — secondary draws as a faint bar behind
  // the value bar (e.g. peak behind avg); tooltipTitle overrides the axis label on hover.
  points: { type: Array, required: true },
  valueLabel: { type: String, required: true },
  secondaryLabel: { type: String, default: null },
  emptyText: { type: String, default: '' },
})

const { locale } = useI18n()

const canvas = ref(null)
const chart = shallowRef(null)
const hasData = ref(true)
const hasSecondary = computed(() => props.secondaryLabel != null)

function render() {
  if (!canvas.value) return
  const accent = cssVar('--accent') || '#5f86c9'
  const border = cssVar('--border') || '#353c49'
  const textMuted = cssVar('--text-muted') || '#9aa3b4'

  hasData.value = props.points.some((p) => p.value > 0 || (p.secondary || 0) > 0)

  // Rounded data-end, square baseline; capped thickness so the band keeps air.
  const bar = { borderRadius: 4, borderSkipped: 'start', maxBarThickness: 24,
    categoryPercentage: 0.8, barPercentage: 0.9, grouped: false }
  const datasets = [{
    ...bar,
    label: props.valueLabel,
    data: props.points.map((p) => p.value),
    backgroundColor: hexA(accent, 0.8),
    hoverBackgroundColor: accent,
    order: 0, // drawn on top
  }]
  if (hasSecondary.value) datasets.push({
    ...bar,
    label: props.secondaryLabel,
    data: props.points.map((p) => p.secondary ?? 0),
    backgroundColor: hexA(accent, 0.2),
    hoverBackgroundColor: hexA(accent, 0.32),
    order: 1,
  })

  if (chart.value) chart.value.destroy()
  chart.value = new Chart(canvas.value.getContext('2d'), {
    type: 'bar',
    data: { labels: props.points.map((p) => p.label), datasets },
    options: {
      locale: locale.value, // tick/tooltip number format follows the UI language
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      // Hover anywhere in a column, not just on the (possibly tiny) bar.
      interaction: { mode: 'index', intersect: false },
      scales: {
        x: {
          grid: { display: false },
          ticks: { color: textMuted, maxRotation: 0, autoSkip: true, maxTicksLimit: 12 },
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
        legend: { display: false }, // HTML legend in the header
        tooltip: {
          itemSort: (a, b) => a.datasetIndex - b.datasetIndex,
          callbacks: {
            title: (items) => {
              const p = props.points[items[0]?.dataIndex]
              return p?.tooltipTitle ?? p?.label ?? ''
            },
            label: (item) => `${item.dataset.label}: ${item.formattedValue}`,
          },
        },
      },
    },
  })
}

watch(() => [props.points, props.valueLabel, props.secondaryLabel, locale.value],
  async () => { await nextTick(); render() }, { deep: true })
onMounted(render)
onUnmounted(() => { if (chart.value) chart.value.destroy() })
</script>

<template>
  <div class="sbc">
    <div class="sbc-head">
      <h3 class="sbc-title">{{ title }}</h3>
      <div v-if="hasSecondary" class="sbc-legend">
        <span><i class="sw sw-value" />{{ valueLabel }}</span>
        <span><i class="sw sw-secondary" />{{ secondaryLabel }}</span>
      </div>
    </div>
    <div class="sbc-body">
      <canvas ref="canvas" />
      <div v-if="!hasData" class="sbc-empty muted">{{ emptyText }}</div>
    </div>
  </div>
</template>

<style scoped>
.sbc-head { display: flex; align-items: baseline; justify-content: space-between; gap: .75rem;
  margin-bottom: .5rem; }
.sbc-title { margin: 0; font-size: .95rem; font-weight: 600; }
.sbc-legend { display: flex; gap: .85rem; font-size: .78rem; color: var(--text-muted); white-space: nowrap; }
.sbc-legend span { display: inline-flex; align-items: center; gap: .35rem; }
.sw { display: inline-block; width: 10px; height: 10px; border-radius: 3px; }
.sw-value { background: color-mix(in srgb, var(--accent) 80%, transparent); }
.sw-secondary { background: color-mix(in srgb, var(--accent) 20%, transparent);
  box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--accent) 45%, transparent); }
.sbc-body { position: relative; height: 200px; }
.sbc-empty {
  position: absolute; inset: 0; display: flex; align-items: center; justify-content: center;
  font-size: .85rem; pointer-events: none;
}
</style>
