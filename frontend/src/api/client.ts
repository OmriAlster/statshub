import axios, { type AxiosRequestConfig } from 'axios'
import { isIbbaWrite, notifyDataChanged, scheduleIbbaFollowUps, topicsForWrite } from './dataSync'

declare module 'axios' {
  interface AxiosRequestConfig {
    // Opt a write out of the app-wide refresh signal - for high-frequency
    // writes (every stat tap while tracking a game) where the screen doing
    // the writing already shows the result, and the caller announces the
    // change itself once it's done (see dataSync.ts).
    skipDataSync?: boolean
  }
}

export const TOKEN_STORAGE_KEY = 'statshub_token'

// In production the API is deployed separately from the frontend, so
// VITE_API_URL points at the real backend URL; in dev it's left unset and
// requests go to the '/api' Vite proxy instead.
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '/api',
})

api.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_STORAGE_KEY)
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// Every write tells the rest of the app what it changed, so every screen
// showing that data refetches - not just the one that made the change.
// Failed writes announce too: a multi-step action can fail halfway (e.g. the
// team got created but linking it didn't), and the screens should show what
// actually got saved.
const announceWrite = (config: AxiosRequestConfig | undefined) => {
  if (!config || config.skipDataSync) return
  const method = (config.method ?? 'get').toLowerCase()
  if (method === 'get' || method === 'head' || method === 'options') return
  const url = config.url ?? ''
  notifyDataChanged(topicsForWrite(url))
  if (isIbbaWrite(url)) scheduleIbbaFollowUps()
}

api.interceptors.response.use(
  (response) => {
    announceWrite(response.config)
    return response
  },
  (error) => {
    announceWrite(error?.config)
    return Promise.reject(error)
  }
)
