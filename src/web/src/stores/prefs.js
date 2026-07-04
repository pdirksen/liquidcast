import { defineStore } from 'pinia'
import { ref } from 'vue'

// Display-only preferences (client side, persisted in localStorage).
export const DATE_FORMATS = [
  { code: 'us', label: 'MM/DD/YYYY HH:MM:SS AM/PM' },
  { code: 'eu', label: 'DD.MM.YYYY HH:MM:SS' },
  { code: 'iso', label: 'ISO 8601' },
]

export const usePrefs = defineStore('prefs', () => {
  const saved = localStorage.getItem('dateFormat')
  const dateFormat = ref(DATE_FORMATS.some((f) => f.code === saved) ? saved : 'us')

  function setDateFormat(v) {
    dateFormat.value = v
    localStorage.setItem('dateFormat', v)
  }

  return { dateFormat, setDateFormat }
})
