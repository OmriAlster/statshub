import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import './App.css'
import { useAuth } from './auth/AuthContext'
import IconSprite from './components/IconSprite'
import Layout from './components/Layout'
import { LiveGameProvider } from './live/LiveGameContext'
import { Analytics } from '@vercel/analytics/react'

const Dashboard = lazy(() => import('./pages/Dashboard'))
const Stats = lazy(() => import('./pages/Stats'))
const GameDetail = lazy(() => import('./pages/GameDetail'))
const PlayerProfile = lazy(() => import('./pages/PlayerProfile'))
const Login = lazy(() => import('./pages/Login'))
const JoinAsPlayer = lazy(() => import('./pages/JoinAsPlayer'))
const SharedPlayerView = lazy(() => import('./pages/SharedPlayerView'))

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth()
  if (loading) return <div className="app-loading">Loading...</div>
  if (!user) return <Navigate to="/login" replace />
  return <>{children}</>
}

function App() {
  const { loading } = useAuth()

  if (loading) {
    return (
      <div className="app-container">
        <div className="app-loading">Loading...</div>
      </div>
    )
  }

  return (
    <LiveGameProvider>
    <IconSprite />
    <Analytics />
    <Suspense fallback={<div className="app-loading">Loading...</div>}>
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/share/:token" element={<SharedPlayerView />} />

      <Route
        path="/join"
        element={
          <ProtectedRoute>
            <Layout>
              <JoinAsPlayer />
            </Layout>
          </ProtectedRoute>
        }
      />

      <Route
        path="/"
        element={
          <ProtectedRoute>
            <Layout>
              <Dashboard />
            </Layout>
          </ProtectedRoute>
        }
      />
      <Route path="/games" element={<Navigate to="/stats" replace />} />
      <Route path="/live-game" element={<Navigate to="/" replace />} />
      <Route
        path="/games/:id"
        element={
          <ProtectedRoute>
            <Layout>
              <GameDetail />
            </Layout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/stats"
        element={
          <ProtectedRoute>
            <Layout>
              <Stats />
            </Layout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/stats/:playerId"
        element={
          <ProtectedRoute>
            <Layout>
              <Stats />
            </Layout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/players"
        element={
          <ProtectedRoute>
            <Layout>
              <PlayerProfile />
            </Layout>
          </ProtectedRoute>
        }
      />

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
    </Suspense>
    </LiveGameProvider>
  )
}

export default App
