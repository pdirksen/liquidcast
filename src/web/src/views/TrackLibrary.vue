<script setup>
import { ref, onMounted, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '../api/client'
import { fmtDuration, fmtBytes } from '../util'
import TreeTable from 'primevue/treetable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import FileUpload from 'primevue/fileupload'
import Dialog from 'primevue/dialog'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const tracks = ref([])
const folders = ref([])
const search = ref('')
const loading = ref(false)
const uploadFolder = ref('')
const expandedKeys = ref({})
const dragging = ref(false)
const dragOverKey = ref(null)
const showNewFolder = ref(false)
const newFolderName = ref('')

async function load() {
  loading.value = true
  try {
    const [tr, fo] = await Promise.all([
      api.get('/tracks', { params: { q: search.value || undefined } }),
      api.get('/tracks/folders'),
    ])
    tracks.value = tr.data
    folders.value = fo.data
  } finally {
    loading.value = false
  }
}
onMounted(load)

// Build a folder tree from each track's relativePath (e.g. "Shows/Morning/a.mp3").
const treeNodes = computed(() => {
  const roots = []
  const folderNodes = new Map() // accumulated path -> folder node
  const folderChildren = (segments) => {
    let children = roots
    let acc = ''
    for (const seg of segments) {
      acc = acc ? `${acc}/${seg}` : seg
      let node = folderNodes.get(acc)
      if (!node) {
        node = { key: `d:${acc}`, data: { name: seg, isFolder: true }, children: [] }
        folderNodes.set(acc, node)
        children.push(node)
      }
      children = node.children
    }
    return children
  }
  // Materialize every folder from disk first so empty folders are shown too.
  for (const dir of folders.value) folderChildren(dir.split('/'))
  for (const tr of tracks.value) {
    const parts = (tr.relativePath || tr.fileName).split('/')
    parts.pop() // drop filename; remaining = folder segments
    folderChildren(parts).push({
      key: `t:${tr.id}`,
      data: { ...tr, isFolder: false, name: tr.title || tr.fileName },
    })
  }
  const sortRec = (arr) => {
    arr.sort((a, b) =>
      a.data.isFolder === b.data.isFolder ? a.data.name.localeCompare(b.data.name) : a.data.isFolder ? -1 : 1)
    arr.forEach((n) => n.children && sortRec(n.children))
  }
  sortRec(roots)
  return roots
})

// Default to fully expanded so the whole library is visible.
watch(treeNodes, (nodes) => {
  const keys = {}
  const walk = (arr) => arr.forEach((n) => { if (n.data.isFolder) { keys[n.key] = true; walk(n.children) } })
  walk(nodes)
  expandedKeys.value = keys
}, { immediate: true })

async function rescan() {
  try {
    const { data } = await api.post('/tracks/rescan')
    toast.add({ severity: 'success', summary: t('tracks.rescanned', { added: data.added, updated: data.updated }), life: 3000 })
    await load()
  } catch {
    toast.add({ severity: 'error', summary: t('tracks.rescanFailed'), life: 4000 })
  }
}

async function onUpload(event) {
  const form = new FormData()
  for (const f of event.files) form.append('files', f)
  form.append('folder', uploadFolder.value || '')
  try {
    const { data } = await api.post('/tracks/upload', form)
    const ok = data.filter((r) => r.track).length
    const dup = data.filter((r) => r.duplicate).length
    const err = data.filter((r) => r.error)
    toast.add({ severity: 'success', summary: t('tracks.uploaded', { n: ok }), detail: dup ? t('tracks.existed', { n: dup }) : '', life: 3000 })
    err.forEach((e) => toast.add({ severity: 'warn', summary: e.fileName, detail: e.error, life: 5000 }))
    await load()
  } catch {
    toast.add({ severity: 'error', summary: t('tracks.uploadFailed'), life: 4000 })
  }
}

function remove(track) {
  confirm.require({
    message: t('tracks.deleteMsg', { name: track.title || track.fileName }),
    header: t('tracks.deleteHeader'),
    icon: 'pi pi-trash',
    acceptProps: { severity: 'danger', label: t('common.delete') },
    accept: async () => {
      try {
        await api.delete(`/tracks/${track.id}`)
        toast.add({ severity: 'success', summary: t('tracks.deleted'), life: 2000 })
        await load()
      } catch (e) {
        toast.add({ severity: 'error', summary: t('tracks.cannotDelete'), detail: e.response?.data?.error, life: 4000 })
      }
    },
  })
}

async function createFolder() {
  const path = newFolderName.value.trim()
  if (!path) return
  try {
    await api.post('/tracks/folders', { path })
    showNewFolder.value = false
    newFolderName.value = ''
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: t('tracks.createFolderFailed'), detail: e.response?.data?.error, life: 4000 })
  }
}

function removeFolder(node) {
  const path = node.key.slice(2) // strip "d:"
  confirm.require({
    message: t('tracks.deleteFolderMsg', { name: path }),
    header: t('tracks.deleteFolderHeader'),
    icon: 'pi pi-folder',
    acceptProps: { severity: 'danger', label: t('common.delete') },
    accept: async () => {
      try {
        await api.delete('/tracks/folders', { params: { path } })
        toast.add({ severity: 'success', summary: t('tracks.folderDeleted'), life: 2000 })
        await load()
      } catch (e) {
        toast.add({ severity: 'error', summary: t('tracks.folderNotEmpty'), detail: e.response?.data?.error, life: 4000 })
      }
    },
  })
}

// --- drag a track leaf onto a folder (or the root dropzone) to move it ---
function onDragStart(event, track) {
  event.dataTransfer.setData('text/plain', String(track.id))
  event.dataTransfer.effectAllowed = 'move'
  // Deferred: mutating the DOM (v-show rootdrop) while dragstart is still
  // in flight makes Chrome cancel the drag before it begins.
  setTimeout(() => { dragging.value = true })
}

// The folder path stamped on the row under the cursor (null if not a folder row).
function folderUnderCursor(event) {
  const row = event.target.closest?.('tr')
  const fnode = row?.querySelector('.fnode')
  return fnode ? fnode.getAttribute('data-folder') : null
}
function onTreeDragOver(event) {
  event.dataTransfer.dropEffect = 'move'
  const path = folderUnderCursor(event)
  dragOverKey.value = path === null ? null : `d:${path}`
}
function onTreeDrop(event) {
  const path = folderUnderCursor(event)
  if (path === null) { dragOverKey.value = null; return } // dropped off a folder row — ignore
  onDrop(event, path)
}
async function onDrop(event, folder) {
  dragging.value = false
  dragOverKey.value = null
  const id = Number(event.dataTransfer.getData('text/plain'))
  if (!id) return
  try {
    await api.post(`/tracks/${id}/move`, { folder })
    toast.add({ severity: 'success', summary: t('tracks.moved'), life: 2000 })
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: t('tracks.moveFailed'), detail: e.response?.data?.error, life: 4000 })
  }
}
</script>

<template>
  <div class="page">
    <h1>{{ t('tracks.title') }}</h1>
    <div class="card">
    <div class="row">
      <span class="p-input-icon-left">
        <InputText v-model="search" :placeholder="t('common.search')" @keyup.enter="load" />
      </span>
      <Button icon="pi pi-search" text @click="load" />
      <Button icon="pi pi-refresh" text :label="t('tracks.rescan')" @click="rescan" />
      <Button icon="pi pi-plus" text :label="t('tracks.newFolder')" @click="showNewFolder = true" />
      <span class="spacer" />
      <InputText v-model="uploadFolder" :placeholder="t('tracks.folderPlaceholder')" style="width:12rem" />
      <FileUpload mode="basic" :multiple="true" accept="audio/mpeg,.mp3" :auto="true"
        :chooseLabel="t('tracks.upload')" :customUpload="true" @uploader="onUpload" />
    </div>

    <div v-show="dragging" class="rootdrop mt" :class="{ over: dragOverKey === '__root__' }"
         @dragenter.prevent="dragOverKey = '__root__'" @dragover.prevent
         @dragleave="dragOverKey = null" @drop.prevent="onDrop($event, '')">
      <i class="pi pi-arrow-up" /> {{ t('tracks.dropToRoot') }}
    </div>

    <!-- Whole-row drop targets: delegation resolves the folder from the row under the cursor,
         so dropping anywhere on a folder row works (not just the name cell). -->
    <div @dragover.prevent="onTreeDragOver" @drop.prevent="onTreeDrop">
    <TreeTable :value="treeNodes" v-model:expandedKeys="expandedKeys" :loading="loading" class="mt" size="small">
      <Column field="name" :header="t('tracks.title_col')" expander>
        <template #body="{ node }">
          <span v-if="node.data.isFolder" class="fnode" :class="{ over: dragOverKey === node.key }"
                :data-folder="node.key.slice(2)">
            <i class="pi pi-folder" style="margin-right:.4rem;color:var(--accent)" />{{ node.data.name }}
          </span>
          <span v-else class="tnode" draggable="true"
                @dragstart="onDragStart($event, node.data)" @dragend="dragging = false">
            <i class="pi pi-bars drag-handle" />{{ node.data.title || node.data.fileName }}
          </span>
        </template>
      </Column>
      <Column :header="t('tracks.artist')">
        <template #body="{ node }"><span v-if="!node.data.isFolder">{{ node.data.artist }}</span></template>
      </Column>
      <Column :header="t('tracks.album')">
        <template #body="{ node }"><span v-if="!node.data.isFolder">{{ node.data.album }}</span></template>
      </Column>
      <Column :header="t('tracks.duration')">
        <template #body="{ node }"><span v-if="!node.data.isFolder">{{ fmtDuration(node.data.durationSec) }}</span></template>
      </Column>
      <Column :header="t('tracks.bitrate')">
        <template #body="{ node }"><span v-if="!node.data.isFolder">{{ node.data.bitrate ? node.data.bitrate + ' kbps' : '—' }}</span></template>
      </Column>
      <Column :header="t('tracks.size')">
        <template #body="{ node }"><span v-if="!node.data.isFolder">{{ fmtBytes(node.data.sizeBytes) }}</span></template>
      </Column>
      <Column style="width:3rem">
        <template #body="{ node }">
          <Button v-if="!node.data.isFolder" icon="pi pi-trash" text severity="danger" size="small" @click="remove(node.data)" />
          <Button v-else-if="!node.children || node.children.length === 0"
                  icon="pi pi-trash" text severity="danger" size="small"
                  v-tooltip.left="t('tracks.deleteFolderHeader')" @click="removeFolder(node)" />
        </template>
      </Column>
    </TreeTable>
    </div>
    </div>

    <Dialog v-model:visible="showNewFolder" :header="t('tracks.createFolder')" modal :style="{ width: '24rem' }">
      <InputText v-model="newFolderName" :placeholder="t('tracks.folderName')" style="width:100%" autofocus
        @keyup.enter="createFolder" />
      <template #footer>
        <Button :label="t('common.cancel')" text @click="showNewFolder = false" />
        <Button :label="t('common.create')" @click="createFolder" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.rootdrop { border: 1px dashed var(--border-strong); border-radius: 8px; padding: .5rem .75rem;
  color: var(--text-muted); background: var(--surface-2); }
.rootdrop.over { border-color: var(--accent); color: var(--text); background: var(--surface-3); }
.tnode { cursor: grab; display: inline-flex; align-items: center; gap: .35rem; }
.tnode:active { cursor: grabbing; }
.tnode .drag-handle { color: var(--text-muted); font-size: .8rem; }
.fnode { display: flex; align-items: center; width: 100%; min-height: 1.8rem; border-radius: 6px; padding: 0 .35rem; }
.fnode.over { background: var(--surface-3); box-shadow: inset 0 0 0 1px var(--accent); }
</style>
