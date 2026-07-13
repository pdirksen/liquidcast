import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'

// Custom timeline-line names, shared app-wide (Schedule, Monitor, EntryDialog).
// Server stores only renamed lines; everything else falls back to the i18n default.
const names = ref({}) // { [line]: customName }
let loaded = false

export function useLineNames() {
  const { t } = useI18n()

  if (!loaded) {
    loaded = true
    api.get('/schedule/lines')
      .then((r) => { names.value = r.data })
      .catch(() => { loaded = false }) // retry on next component using the composable
  }

  function lineLabel(line) {
    if (line == null) return null
    return names.value[line]
      || (line === 0 ? t('schedule.lineFallback') : t('schedule.lineName', { n: line }))
  }

  async function renameLine(line, name) {
    const trimmed = (name || '').trim()
    await api.put(`/schedule/lines/${line}`, { name: trimmed })
    const next = { ...names.value }
    if (trimmed) next[line] = trimmed
    else delete next[line] // empty name resets to the default label
    names.value = next
  }

  return { lineLabel, renameLine }
}
