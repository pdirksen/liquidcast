<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'
import { fmtDuration } from '../util'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'

const router = useRouter()
const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const playlists = ref([])
const search = ref('')
const showCreate = ref(false)
const newName = ref('')

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  return q ? playlists.value.filter((p) => (p.name || '').toLowerCase().includes(q)) : playlists.value
})

async function load() { playlists.value = (await api.get('/playlists')).data }
onMounted(load)

async function create() {
  if (!newName.value.trim()) return
  const { data } = await api.post('/playlists', { name: newName.value.trim() })
  showCreate.value = false
  newName.value = ''
  router.push(`/playlists/${data.id}`)
}

function deleteEmpty() {
  const empties = playlists.value.filter((p) => !p.itemCount)
  if (!empties.length) {
    toast.add({ severity: 'info', summary: t('playlists.deleteEmptyNone'), life: 2000 })
    return
  }
  confirm.require({
    message: t('playlists.deleteEmptyMsg', { count: empties.length }),
    header: t('playlists.deleteEmptyHeader'),
    icon: 'pi pi-trash',
    acceptProps: { severity: 'danger', label: t('common.delete') },
    accept: async () => {
      await Promise.all(empties.map((p) => api.delete(`/playlists/${p.id}`)))
      toast.add({ severity: 'success', summary: t('playlists.deleteEmptyDone', { count: empties.length }), life: 2500 })
      await load()
    },
  })
}

function remove(p) {
  confirm.require({
    message: t('playlists.deleteMsg', { name: p.name }),
    header: t('playlists.deleteHeader'),
    icon: 'pi pi-trash',
    acceptProps: { severity: 'danger', label: t('common.delete') },
    accept: async () => {
      await api.delete(`/playlists/${p.id}`)
      toast.add({ severity: 'success', summary: t('playlists.deleted'), life: 2000 })
      await load()
    },
  })
}
</script>

<template>
  <div class="page">
    <h1>Playlists</h1>
    <div class="card">
    <div class="row">
      <InputText v-model="search" :placeholder="t('common.search')" size="small" />
      <span class="spacer" />
      <Button :label="t('playlists.deleteEmpty')" icon="pi pi-trash" severity="danger" outlined
        @click="deleteEmpty" />
      <Button :label="t('playlists.newPlaylist')" icon="pi pi-plus" @click="showCreate = true" />
    </div>

    <DataTable :value="filtered" class="mt" stripedRows rowHover size="small"
      paginator :rows="10" :rowsPerPageOptions="[10, 25, 50]">
      <Column field="name" :header="t('playlists.name')" />
      <Column field="itemCount" header="Tracks" />
      <Column :header="t('playlists.runtime')">
        <template #body="{ data }">{{ fmtDuration(data.totalDurationSec) }}</template>
      </Column>
      <Column style="width:8rem">
        <template #body="{ data }">
          <Button icon="pi pi-pencil" text size="small" @click="router.push(`/playlists/${data.id}`)" />
          <Button icon="pi pi-trash" text severity="danger" size="small" @click="remove(data)" />
        </template>
      </Column>
    </DataTable>
    </div>

    <Dialog v-model:visible="showCreate" :header="t('playlists.newPlaylist')" modal :style="{ width: '24rem' }">
      <InputText v-model="newName" :placeholder="t('playlists.playlistName')" class="w-full" autofocus
        style="width:100%" @keyup.enter="create" />
      <template #footer>
        <Button :label="t('common.cancel')" text @click="showCreate = false" />
        <Button :label="t('common.create')" @click="create" />
      </template>
    </Dialog>
  </div>
</template>
