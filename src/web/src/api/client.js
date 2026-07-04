import axios from 'axios'

// Same-origin in production; the Vite proxy forwards /api in dev.
export const api = axios.create({
  baseURL: '/api',
  withCredentials: true,
})

let onUnauthorized = null
export function setUnauthorizedHandler(fn) { onUnauthorized = fn }

api.interceptors.response.use(
  (r) => r,
  (err) => {
    if (err.response?.status === 401 && onUnauthorized) onUnauthorized()
    return Promise.reject(err)
  },
)
