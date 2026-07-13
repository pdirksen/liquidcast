import { createRouter, createWebHistory } from 'vue-router'
import { useAuth } from '../stores/auth'

const routes = [
  { path: '/login', name: 'login', component: () => import('../views/Login.vue'), meta: { public: true } },
  { path: '/', redirect: '/monitor' },
  { path: '/monitor', name: 'monitor', component: () => import('../views/Monitor.vue') },
  { path: '/stats', name: 'stats', component: () => import('../views/Statistics.vue') },
  { path: '/tracks', name: 'tracks', component: () => import('../views/TrackLibrary.vue') },
  { path: '/playlists', name: 'playlists', component: () => import('../views/Playlists.vue') },
  { path: '/playlists/:id', name: 'playlist-editor', component: () => import('../views/PlaylistEditor.vue') },
  { path: '/schedule', name: 'schedule', component: () => import('../views/Schedule.vue') },
  { path: '/settings', name: 'settings', component: () => import('../views/Settings.vue') },
]

const router = createRouter({ history: createWebHistory(), routes })

router.beforeEach(async (to) => {
  const auth = useAuth()
  if (!auth.ready) await auth.fetchMe()
  if (!to.meta.public && !auth.user) return { name: 'login', query: { redirect: to.fullPath } }
  if (to.name === 'login' && auth.user) return { name: 'monitor' }
})

export default router
