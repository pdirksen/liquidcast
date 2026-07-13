// Shared helpers for Chart.js components (the app is single-theme dark).

// Read a CSS custom property off the document root.
export function cssVar(name) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim()
}

// Turn "#rrggbb" into an rgba() with the given alpha (chart.js gradient stops).
export function hexA(hex, alpha) {
  const m = hex.replace('#', '')
  const full = m.length === 3 ? m.split('').map((c) => c + c).join('') : m
  const n = parseInt(full, 16)
  const r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}
