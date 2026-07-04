<script setup>
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'
import { formatDateTime, datePickerProps } from '../util'
import { usePrefs } from '../stores/prefs'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import Checkbox from 'primevue/checkbox'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'

const { t } = useI18n()
const prefs = usePrefs()
const dpProps = computed(() => datePickerProps(prefs.dateFormat))
const toast = useToast()
const confirm = useConfirm()
const slots = ref([])
const playlists = ref([])
const showDialog = ref(false)
const editing = ref(null)

const recurrences = computed(() => [
  { label: t('schedule.oneOff'), value: 0 },
  { label: t('schedule.daily'), value: 1 },
  { label: t('schedule.weekly'), value: 2 },
])
const recLabel = (v) => recurrences.value.find((r) => r.value === v)?.label || '—'

const form = ref(blank())
function blank() {
  const now = new Date()
  const start = new Date(now.getTime() + 5 * 60000)
  const end = new Date(start.getTime() + 60 * 60000)
  return { playlistId: null, start, end, recurrence: 0, hardCut: false, loop: true, allDay: false }
}

const isMidnight = (d) => d.getHours() === 0 && d.getMinutes() === 0 && d.getSeconds() === 0
const atMidnight = (d) => { const x = new Date(d); x.setHours(0, 0, 0, 0); return x }
const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x }

async function load() {
  const [s, p] = await Promise.all([api.get('/schedule'), api.get('/playlists')])
  slots.value = s.data
  playlists.value = p.data.map((x) => ({ label: x.name, value: x.id }))
}
onMounted(load)

function openNew() { editing.value = null; form.value = blank(); showDialog.value = true }
function openEdit(slot) {
  editing.value = slot
  const start = new Date(slot.startUtc)
  const end = new Date(slot.endUtc)
  // Detect an all-day slot: both bounds on local midnight. Display the end as the
  // last included day (stored end is the exclusive next-midnight).
  const allDay = isMidnight(start) && isMidnight(end)
  form.value = {
    playlistId: slot.playlistId,
    start,
    end: allDay ? addDays(end, -1) : end,
    recurrence: slot.recurrence, hardCut: slot.hardCut, loop: slot.loop, allDay,
  }
  showDialog.value = true
}

async function save() {
  let start = new Date(form.value.start)
  let end = new Date(form.value.end)
  if (form.value.allDay) {
    // Full days: snap to local midnight; end is the exclusive next midnight after the last day.
    start = atMidnight(start)
    end = addDays(atMidnight(end), 1)
  }
  const body = {
    playlistId: form.value.playlistId,
    startUtc: start.toISOString(),
    endUtc: end.toISOString(),
    recurrence: form.value.recurrence,
    hardCut: form.value.hardCut,
    loop: form.value.loop,
  }
  try {
    if (editing.value) await api.put(`/schedule/${editing.value.id}`, body)
    else await api.post('/schedule', body)
    showDialog.value = false
    toast.add({ severity: 'success', summary: t('schedule.saved'), life: 2000 })
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: t('schedule.cannotSave'), detail: e.response?.data?.error, life: 4000 })
  }
}

function remove(slot) {
  confirm.require({
    message: t('schedule.deleteMsg'),
    header: t('schedule.deleteHeader'), icon: 'pi pi-trash',
    acceptProps: { severity: 'danger', label: t('common.delete') },
    accept: async () => { await api.delete(`/schedule/${slot.id}`); await load() },
  })
}

const fmt = (iso) => formatDateTime(iso, prefs.dateFormat)
</script>

<template>
  <div class="page">
    <h1>{{ t('schedule.title') }}</h1>
    <div class="card">
    <div class="row">
      <span class="spacer" />
      <Button :label="t('schedule.addSlot')" icon="pi pi-plus" @click="openNew" />
    </div>

    <DataTable :value="slots" class="mt" stripedRows size="small">
      <Column field="playlistName" :header="t('schedule.playlist')" />
      <Column :header="t('schedule.start')"><template #body="{ data }">{{ fmt(data.startUtc) }}</template></Column>
      <Column :header="t('schedule.end')"><template #body="{ data }">{{ fmt(data.endUtc) }}</template></Column>
      <Column :header="t('schedule.repeat')"><template #body="{ data }"><Tag :value="recLabel(data.recurrence)" /></template></Column>
      <Column :header="t('schedule.cut')"><template #body="{ data }">{{ data.hardCut ? t('schedule.hard') : t('schedule.crossfade') }}</template></Column>
      <Column style="width:7rem">
        <template #body="{ data }">
          <Button icon="pi pi-pencil" text size="small" @click="openEdit(data)" />
          <Button icon="pi pi-trash" text severity="danger" size="small" @click="remove(data)" />
        </template>
      </Column>
    </DataTable>
    </div>

    <Dialog v-model:visible="showDialog" :header="editing ? t('schedule.editSlot') : t('schedule.newSlot')" modal :style="{ width: '28rem' }">
      <div class="form">
        <label>{{ t('schedule.playlist') }}</label>
        <Select v-model="form.playlistId" :options="playlists" optionLabel="label" optionValue="value"
          :placeholder="t('schedule.selectPlaylist')" />
        <div class="row" style="grid-column:1/-1">
          <Checkbox v-model="form.allDay" :binary="true" inputId="allday" />
          <label for="allday" style="margin:0">{{ t('schedule.allDay') }}</label>
        </div>
        <label>{{ t('schedule.start') }}</label>
        <DatePicker v-model="form.start" :showTime="!form.allDay" :showSeconds="!form.allDay" fluid v-bind="dpProps" />
        <label>{{ t('schedule.end') }}</label>
        <DatePicker v-model="form.end" :showTime="!form.allDay" :showSeconds="!form.allDay" fluid v-bind="dpProps" />
        <label>{{ t('schedule.repeat') }}</label>
        <Select v-model="form.recurrence" :options="recurrences" optionLabel="label" optionValue="value" />
        <div class="row mt">
          <Checkbox v-model="form.loop" :binary="true" inputId="loop" />
          <label for="loop" style="margin:0">{{ t('schedule.loopFill') }}</label>
        </div>
        <div class="row">
          <Checkbox v-model="form.hardCut" :binary="true" inputId="hc" />
          <label for="hc" style="margin:0">{{ t('schedule.hardCutStart') }}</label>
        </div>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" text @click="showDialog = false" />
        <Button :label="t('common.save')" @click="save" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.form { display: flex; flex-direction: column; gap: .4rem; }
.form > label { font-size: .8rem; color: var(--text-muted); margin-top: .4rem; }
.dt { background: var(--surface-2); border: 1px solid var(--border); color: var(--text); border-radius: 8px; padding: .5rem; color-scheme: dark; }
</style>
