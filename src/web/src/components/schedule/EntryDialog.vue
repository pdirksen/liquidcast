<script setup>
import { computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { datePickerProps, fmtDuration, formatDateTime } from '../../util'
import { usePrefs } from '../../stores/prefs'
import { classifyOverlap, LINE_ORDER } from './scheduleMath'
import { useLineNames } from '../../composables/lineNames'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import Checkbox from 'primevue/checkbox'

const props = defineProps({
  visible: { type: Boolean, required: true },
  // { id?, trackId, title, artist, effDurationSec, line, start:Date, override, cueInSec, cueOutSec, crossfadeSec }
  draft: { type: Object, default: null },
  entries: { type: Array, required: true },
})
const emit = defineEmits(['update:visible', 'save', 'remove'])

const { t } = useI18n()
const prefs = usePrefs()
const dpProps = computed(() => datePickerProps(prefs.dateFormat))

const { lineLabel } = useLineNames()
const lineOptions = computed(() => LINE_ORDER.map((line) => ({
  label: lineLabel(line),
  value: line,
})))

const endDate = computed(() => {
  if (!props.draft?.start) return null
  return new Date(new Date(props.draft.start).getTime() + (props.draft.effDurationSec || 0) * 1000)
})

const conflict = computed(() => {
  if (!props.draft?.start) return { sameLine: false, crossLine: false, needsOverride: false }
  const startMs = new Date(props.draft.start).getTime()
  return classifyOverlap(props.entries, {
    id: props.draft.id ?? null,
    line: props.draft.line,
    startMs,
    endMs: startMs + (props.draft.effDurationSec || 0) * 1000,
  })
})

// Override is only meaningful (and only allowed) while a cross-line conflict exists.
watch(conflict, (c) => {
  if (props.draft && !c.crossLine) props.draft.override = false
})

const canSave = computed(() =>
  !!props.draft && !conflict.value.sameLine &&
  (!conflict.value.needsOverride || props.draft.override))

const fmtEnd = computed(() => (endDate.value ? formatDateTime(endDate.value.toISOString(), prefs.dateFormat) : ''))
</script>

<template>
  <Dialog :visible="visible" @update:visible="emit('update:visible', $event)" modal
    :header="draft?.id ? t('schedule.editEntry') : t('schedule.newEntry')" :style="{ width: '26rem' }">
    <div v-if="draft" class="form">
      <label>{{ t('schedule.track') }}</label>
      <div class="track-label">
        <b>{{ draft.title }}</b>
        <span v-if="draft.artist" class="muted"> · {{ draft.artist }}</span>
        <span class="muted"> · {{ fmtDuration(draft.effDurationSec) }}</span>
      </div>

      <label>{{ t('schedule.line') }}</label>
      <Select v-model="draft.line" :options="lineOptions" optionLabel="label" optionValue="value" />

      <label>{{ t('schedule.start') }}</label>
      <DatePicker v-model="draft.start" showTime showSeconds fluid v-bind="dpProps" />

      <label>{{ t('schedule.end') }}</label>
      <div class="muted end">{{ fmtEnd }}</div>

      <div class="row mt">
        <Checkbox v-model="draft.override" :binary="true" inputId="ovr" :disabled="!conflict.crossLine" />
        <label for="ovr" style="margin:0" :class="{ dim: !conflict.crossLine }">{{ t('schedule.override') }}</label>
      </div>
      <small class="muted">{{ t('schedule.overrideHint') }}</small>

      <div v-if="conflict.sameLine" class="warn err">
        <i class="pi pi-times-circle" /> {{ t('schedule.overlapSameLine') }}
      </div>
      <div v-else-if="conflict.needsOverride && !draft.override" class="warn">
        <i class="pi pi-exclamation-triangle" /> {{ t('schedule.overlapNeedsOverride') }}
      </div>
    </div>
    <template #footer>
      <Button v-if="draft?.id" :label="t('common.delete')" severity="danger" text
        @click="emit('remove', draft.id)" />
      <span style="flex:1" />
      <Button :label="t('common.cancel')" text @click="emit('update:visible', false)" />
      <Button :label="t('common.save')" :disabled="!canSave" @click="emit('save')" />
    </template>
  </Dialog>
</template>

<style scoped>
.form { display: flex; flex-direction: column; gap: .4rem; }
.form > label { font-size: .8rem; color: var(--text-muted); margin-top: .4rem; }
.track-label { padding: .4rem .6rem; background: var(--surface-2); border: 1px solid var(--border); border-radius: 8px; }
.end { padding: .2rem 0; }
.dim { color: var(--text-dim); }
.warn { display: flex; align-items: center; gap: .5rem; margin-top: .6rem; padding: .5rem .6rem; border-radius: 8px;
  background: rgba(251, 191, 36, .12); border: 1px solid rgba(251, 191, 36, .4); font-size: .85rem; }
.warn.err { background: rgba(239, 68, 68, .12); border-color: rgba(239, 68, 68, .4); }
</style>
