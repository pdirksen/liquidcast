// Client-side mirror of Services/ScheduleMath.cs — keep the two in sync.

// Audible duration of a (dragged or scheduled) track: cue-out (or file end) minus cue-in.
export function effDurationSec({ durationSec, cueInSec, cueOutSec }) {
  const total = durationSec || 0
  const end = cueOutSec > 0 ? Math.min(cueOutSec, total > 0 ? total : cueOutSec) : total
  const start = cueInSec > 0 ? cueInSec : 0
  return Math.max(0, end - start)
}

// Half-open interval overlap — touching entries are adjacent, not overlapping.
export function overlaps(aStart, aEnd, bStart, bEnd) {
  return aStart < bEnd && bStart < aEnd
}

// Classifies a candidate window against existing entries (excluding its own id).
// - sameLine: overlap on the same line → never allowed.
// - crossLine: any overlap on another line → the override checkbox becomes available.
// - needsOverride: overlap with a plain (non-override) entry on another line → save
//   requires the override flag. Overlapping an override entry is already sanctioned.
export function classifyOverlap(entries, { id, line, startMs, endMs }) {
  let sameLine = false
  let crossLine = false
  let needsOverride = false
  for (const o of entries) {
    if (id != null && o.id === id) continue
    const oS = new Date(o.startUtc).getTime()
    const oE = new Date(o.endUtc).getTime()
    if (!overlaps(startMs, endMs, oS, oE)) continue
    if (o.line === line) sameLine = true
    else { crossLine = true; if (!o.override) needsOverride = true }
  }
  return { sameLine, crossLine, needsOverride }
}

// Display order: fallback line on top, then Priority 1 (highest) … Priority 4.
export const LINE_ORDER = [0, 1, 2, 3, 4]
