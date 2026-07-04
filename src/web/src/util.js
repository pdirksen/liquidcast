export function fmtDuration(sec) {
  sec = Math.max(0, Math.round(sec || 0))
  const h = Math.floor(sec / 3600)
  const m = Math.floor((sec % 3600) / 60)
  const s = sec % 60
  const mm = String(m).padStart(2, '0')
  const ss = String(s).padStart(2, '0')
  return h > 0 ? `${h}:${mm}:${ss}` : `${m}:${ss}`
}

const pad = (n) => String(n).padStart(2, '0')

// Format a UTC ISO timestamp in local time according to the chosen display format.
export function formatDateTime(iso, fmt = 'us') {
  if (!iso) return ''
  const d = new Date(iso)
  const Y = d.getFullYear(), Mo = pad(d.getMonth() + 1), Da = pad(d.getDate())
  const H = d.getHours(), Mi = pad(d.getMinutes()), S = pad(d.getSeconds())
  if (fmt === 'eu') return `${Da}.${Mo}.${Y} ${pad(H)}:${Mi}:${S}`
  if (fmt === 'iso') return `${Y}-${Mo}-${Da}T${pad(H)}:${Mi}:${S}`
  const ap = H >= 12 ? 'PM' : 'AM'
  const h12 = H % 12 === 0 ? 12 : H % 12
  return `${Mo}/${Da}/${Y} ${pad(h12)}:${Mi}:${S} ${ap}`
}

// PrimeVue DatePicker props matching a display format.
export function datePickerProps(fmt) {
  if (fmt === 'eu') return { dateFormat: 'dd.mm.yy', hourFormat: '24' }
  if (fmt === 'iso') return { dateFormat: 'yy-mm-dd', hourFormat: '24' }
  return { dateFormat: 'mm/dd/yy', hourFormat: '12' }
}

export function fmtBytes(b) {
  if (!b) return '0 B'
  const u = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(b) / Math.log(1024))
  return `${(b / Math.pow(1024, i)).toFixed(i ? 1 : 0)} ${u[i]}`
}

// <input type="datetime-local"> uses local time; convert to/from UTC ISO.
export function localToUtcIso(local) {
  if (!local) return null
  return new Date(local).toISOString()
}

export function utcIsoToLocalInput(iso) {
  if (!iso) return ''
  const d = new Date(iso)
  const off = d.getTimezoneOffset() * 60000
  return new Date(d.getTime() - off).toISOString().slice(0, 16)
}
