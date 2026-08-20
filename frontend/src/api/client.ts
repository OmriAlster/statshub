import axios from 'axios'

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
