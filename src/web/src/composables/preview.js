import { ref, onUnmounted } from 'vue'

// Local preview playback (NOT the stream): one <audio> element for the whole app,
// so starting a track anywhere stops whatever was previewing before. The file is
// served by GET /api/tracks/{id}/file — same origin, so the auth cookie rides along
// and the endpoint's range support keeps seeking cheap.
const currentId = ref(null) // track whose file is loaded (playing or paused)
const playing = ref(false)
const loading = ref(false)
let audio = null

function el() {
  if (audio) return audio
  audio = new Audio()
  audio.preload = 'none'
  audio.addEventListener('playing', () => { loading.value = false; playing.value = true })
  audio.addEventListener('pause', () => { playing.value = false })
  audio.addEventListener('ended', () => { playing.value = false; loading.value = false })
  return audio
}

function stop() {
  if (audio) { audio.pause(); audio.removeAttribute('src'); audio.load() }
  currentId.value = null
  playing.value = false
  loading.value = false
}

/// Toggle preview of a track. Returns false when playback could not start
/// (missing file, decode error) so the caller can surface it.
async function toggle(trackId) {
  const a = el()
  if (currentId.value === trackId) {
    // Same track: pause/resume in place, keeping the position.
    if (playing.value) { a.pause(); return true }
    try { await a.play(); return true } catch { stop(); return false }
  }
  a.pause()
  a.src = `/api/tracks/${trackId}/file`
  currentId.value = trackId
  loading.value = true
  playing.value = false
  try {
    await a.play()
    return true
  } catch {
    stop()
    return false
  }
}

/// Jump forward in the running preview. Clamped to just before the end so the
/// element fires 'ended' instead of throwing on an out-of-range seek.
function skip(seconds = 10) {
  if (!audio || currentId.value == null) return
  const dur = audio.duration
  const next = audio.currentTime + seconds
  audio.currentTime = Number.isFinite(dur) ? Math.min(next, Math.max(0, dur - 0.25)) : next
}

/// Must be called from setup(): leaving the view stops the preview.
export function usePreview() {
  onUnmounted(stop)
  return {
    currentId,
    playing,
    loading,
    toggle,
    skip,
    stop,
    isActive: (id) => currentId.value === id,
    isPlaying: (id) => currentId.value === id && playing.value,
    isLoading: (id) => currentId.value === id && loading.value,
  }
}
