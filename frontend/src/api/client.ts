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

// A signed-in request refused as unauthorized means this login was ended
// ("Log out of all devices" on another phone, or it expired) - go back to
// the sign-in page instead of showing empty pages and errors. Not for the
// sign-in calls themselves, where 401 just means a wrong password.
const SIGN_IN_CALLS = ['/auth/login', '/auth/register', '/auth/google', '/auth/dev-login']
api.interceptors.response.use(undefined, (error) => {
  const sentToken = !!error?.config?.headers?.Authorization
  const url: string = error?.config?.url ?? ''
  if (error?.response?.status === 401 && sentToken && !SIGN_IN_CALLS.some((path) => url.endsWith(path))) {
    localStorage.removeItem(TOKEN_STORAGE_KEY)
    if (!window.location.pathname.startsWith('/login')) window.location.assign('/login?ended=1')
  }
  return Promise.reject(error)
})
