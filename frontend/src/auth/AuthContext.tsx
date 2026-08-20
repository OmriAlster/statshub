import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, TOKEN_STORAGE_KEY } from '../api/client'
import type { UserDto } from '../api/types'

interface AuthContextValue {
  user: UserDto | null
  loading: boolean
  loginWithGoogle: (idToken: string) => Promise<void>
  loginWithPassword: (email: string, password: string) => Promise<void>
  register: (email: string, password: string, firstName: string, lastName: string) => Promise<void>
  devLogin: (email: string, firstName: string, lastName: string) => Promise<void>
  logout: () => void
  refreshUser: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null)
  const [loading, setLoading] = useState(true)

  const refreshUser = async () => {
    const token = localStorage.getItem(TOKEN_STORAGE_KEY)
    if (!token) {
      setUser(null)
      setLoading(false)
      return
    }
    try {
      const { data } = await api.get<UserDto>('/auth/me')
      setUser(data)
    } catch {
      localStorage.removeItem(TOKEN_STORAGE_KEY)
      setUser(null)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    refreshUser()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const loginWithGoogle = async (idToken: string) => {
    const { data } = await api.post('/auth/google', { idToken })
    localStorage.setItem(TOKEN_STORAGE_KEY, data.token)
    setUser(data.user)
  }

  const loginWithPassword = async (email: string, password: string) => {
    const { data } = await api.post('/auth/login', { email, password })
    localStorage.setItem(TOKEN_STORAGE_KEY, data.token)
    setUser(data.user)
  }

  const register = async (email: string, password: string, firstName: string, lastName: string) => {
    const { data } = await api.post('/auth/register', { email, password, firstName, lastName })
    localStorage.setItem(TOKEN_STORAGE_KEY, data.token)
    setUser(data.user)
  }

  const devLogin = async (email: string, firstName: string, lastName: string) => {
    const { data } = await api.post('/auth/dev-login', { email, firstName, lastName })
    localStorage.setItem(TOKEN_STORAGE_KEY, data.token)
    setUser(data.user)
  }

  const logout = () => {
    localStorage.removeItem(TOKEN_STORAGE_KEY)
    setUser(null)
  }

  const value = useMemo(
    () => ({ user, loading, loginWithGoogle, loginWithPassword, register, devLogin, logout, refreshUser }),
    [user, loading]
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
