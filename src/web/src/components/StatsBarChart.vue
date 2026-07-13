<script setup>
import { ref, shallowRef, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { cssVar, hexA } from '../utils/chart'
import { Chart, BarController, BarElement, LinearScale, CategoryScale, Tooltip } from 'chart.js'

Chart.register(BarController, BarElement, LinearScale, CategoryScale, Tooltip)

const props = defineProps({
  title: { type: String, required: true },
  // [{ label, value, secondary? }] — secondary shows as an extra tooltip line.
  points: { type: Array, required: true },
  valueLabel: { type: String, required: true },
  secondaryLabel: { type: String, default: null },
  emptyText: { type: String, default: '' },
})

const canvas = ref(null)
const chart = shallowRef(null)
const hasData = ref(true)

function render() {
  if (!canvas.value) return
  const accent = cssVar('--accent') || '#5f86c9'
  const border = cssVar('--border') || '#353c49'
  const textMuted = cssVar('--text-muted') || '#9aa3b4'

  hasData.value = props.points.some((p) => p.value > 0 || (p.secondary || 0) > 0)

  if (chart.value) chart.value.destroy()
  chart.value = new Chart(canvas.value.getContext('2d'), {
    type: 'bar',
    data: {
      labels: props.points.map((p) => p.label),
      datasets: [{
        data: props.points.map((p) => p.value),
        backgroundColor: hexA(accent, 0.75),
        hoverBackgroundColor: accent,
        // Rounded data-end, square baseline; capped thickness so the band keeps air.
        borderRadius: 4,
        borderSkipped: 'start',
        maxBarThickness: 24,
        categoryPercentage: 0.8,
        barPercentage: 0.9,
      }],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      scales: {
        x: {
          grid: { display: false },
          ticks: { color: textMuted, maxRotation: 0, autoSkip: true, maxTicksLimit: 16 },
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
        legend: { display: false }, // single series — the title names it
        tooltip: {
          callbacks: {
            label: (item) => {
              const p = props.points[item.dataIndex]
              const lines = [`${props.valueLabel}: ${p.value}`]
              if (props.secondaryLabel != null && p.secondary != null)
                lines.push(`${props.secondaryLabel}: ${p.secondary}`)
              return lines
            },
          },
        },
      },
    },
  })
}

watch(() => props.points, async () => { await nextTick(); render() }, { deep: true })
onMounted(render)
onUnmounted(() => { if (chart.value) chart.value.destroy() })
</script>

<template>
  <div class="sbc">
    <h3 class="card-title">{{ title }}</h3>
    <div class="sbc-body">
      <canvas ref="canvas" />
      <div v-if="!hasData" class="sbc-empty muted">{{ emptyText }}</div>
    </div>
  </div>
</template>

<style scoped>
.sbc-body { position: relative; height: 200px; }
.sbc-empty {
  position: absolute; inset: 0; display: flex; align-items: center; justify-content: center;
  font-size: .85rem; pointer-events: none;
}
</style>
