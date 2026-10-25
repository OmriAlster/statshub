import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import './App.css'
import { useAuth } from './auth/AuthContext'
import IconSprite from './components/IconSprite'
import Layout from './components/Layout'
import { LiveGameProvider } from './live/LiveGameContext'
import { Analytics } from '@vercel/analytics/react'
import BouncingBall from './components/BouncingBall'

const Dashboard = lazy(() => import('./pages/Dashboard'))
const Stats = lazy(() => import('./pages/Stats'))
const GameDetail = lazy(() => import('./pages/GameDetail'))
const PlayerProfile = lazy(() => import('./pages/PlayerProfile'))
const Login = lazy(() => import('./pages/Login'))
const JoinAsPlayer = lazy(() => import('./pages/JoinAsPlayer'))
const SharedPlayerView = lazy(() => import('./pages/SharedPlayerView'))

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth()
  if (loading) return <div className="app-loading"><BouncingBall size="lg" /></div>
  if (!user) return <Navigate to="/login" replace />
  return <>{children}</>
}

// A page for signed-in users, inside the app's header and navigation.
const signedIn = (page: React.ReactNode) => <ProtectedRoute><Layout>{page}</Layout></ProtectedRoute>

function App() {
  const { loading } = useAuth()

  if (loading) {
    return (
      <div className="app-container">
        <div className="app-loading"><BouncingBall size="lg" /></div>
      </div>
    )
  }

  return (
    <LiveGameProvider>
    <IconSprite />
    <Analytics />
    <Suspense fallback={<div className="app-loading"><BouncingBall size="lg" /></div>}>
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/share/:token" element={<SharedPlayerView />} />
      <Route path="/share/:token/games/:gameId" element={<SharedPlayerView />} />

      <Route path="/join" element={signedIn(<JoinAsPlayer />)} />

      <Route path="/" element={signedIn(<Dashboard />)} />
      <Route path="/games" element={<Navigate to="/stats" replace />} />
      <Route path="/live-game" element={<Navigate to="/" replace />} />
      <Route path="/games/:id" element={signedIn(<GameDetail />)} />
      <Route path="/stats" element={signedIn(<Stats />)} />
      <Route path="/stats/:playerId" element={signedIn(<Stats />)} />
      <Route path="/players" element={signedIn(<PlayerProfile />)} />

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
    </Suspense>
    </LiveGameProvider>
  )
}

export default App
