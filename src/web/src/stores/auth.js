import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '../api/client'

export const useAuth = defineStore('auth', () => {
  const user = ref(null)
  const ready = ref(false)

  async function fetchMe() {
    try {
      const { data } = await api.get('/auth/me')
      user.value = data
    } catch {
      user.value = null
    } finally {
      ready.value = true
    }
  }

  async function login(username, password) {
    const { data } = await api.post('/auth/login', { username, password })
    user.value = data
  }

  async function logout() {
    try { await api.post('/auth/logout') } catch { /* ignore */ }
    user.value = null
  }

  return { user, ready, fetchMe, login, logout }
})
