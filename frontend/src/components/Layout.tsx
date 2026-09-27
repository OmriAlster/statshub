import { useState, lazy, Suspense, type ReactNode } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import InstallAppBanner from './InstallAppBanner'
import NotificationBanner from './NotificationBanner'

const LiveGameWidget = lazy(() => import('../live/LiveGameWidget'))

export default function Layout({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const isPlayerRole = user?.role === 'Player'

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  return (
    <div className="app-container">
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark"><svg className="icon" style={{ stroke: '#2b1400' }}><use href="#i-ball" /></svg></span>
          StatsHub
        </div>
        {user && (
          <div className="user-info">
            <UserAvatar url={user.profilePictureUrl} initials={`${user.firstName?.[0] ?? ''}${user.lastName?.[0] ?? ''}`} />
            <span>{user.firstName} {user.lastName}</span> {isPlayerRole && <span className="role-badge">Player</span>}
            <button onClick={handleLogout} className="logout-btn" aria-label="Log out">
              <svg className="icon"><use href="#i-logout" /></svg>
            </button>
          </div>
        )}
      </header>

      <nav className="app-nav">
        <div className="app-nav-brand">
          <span className="brand-mark"><svg className="icon" style={{ stroke: '#2b1400' }}><use href="#i-ball" /></svg></span>
          StatsHub
        </div>

        <NavLink to="/" end className={({ isActive }) => `nav-btn ${isActive ? 'active' : ''}`}>
          <svg className="icon"><use href="#i-home" /></svg>
          <span>Dashboard</span>
        </NavLink>
        <NavLink to="/stats" className={({ isActive }) => `nav-btn ${isActive ? 'active' : ''}`}>
          <svg className="icon"><use href="#i-chart" /></svg>
          <span>Profiles</span>
        </NavLink>
        <NavLink to="/players" className={({ isActive }) => `nav-btn ${isActive ? 'active' : ''}`}>
          <svg className="icon"><use href={isPlayerRole ? '#i-user' : '#i-users'} /></svg>
          <span>{isPlayerRole ? 'My Profile' : 'Players'}</span>
        </NavLink>

        {user && (
          <div className="app-nav-user">
            <UserAvatar url={user.profilePictureUrl} initials={`${user.firstName?.[0] ?? ''}${user.lastName?.[0] ?? ''}`} />
            <div className="app-nav-user-text">
              <span className="u-name">{user.firstName} {user.lastName}</span>
              {isPlayerRole && <span className="role-badge">Player</span>}
            </div>
            <button onClick={handleLogout} className="logout-btn" aria-label="Log out">
              <svg className="icon"><use href="#i-logout" /></svg>
            </button>
          </div>
        )}
      </nav>

      <main className="app-content">
        <InstallAppBanner />
        <NotificationBanner />
        {children}
      </main>

      {!isPlayerRole && (
        <Suspense fallback={null}>
          <LiveGameWidget />
        </Suspense>
      )}
    </div>
  )
}

// The account photo (e.g. from Google) can fail to load - a broken-image
// icon in the header looked like a bug, so fall back to initials instead.
function UserAvatar({ url, initials }: { url?: string | null; initials: string }) {
  const [failed, setFailed] = useState(false)
  if (url && !failed) return <img className="avatar" src={url} alt="" onError={() => setFailed(true)} referrerPolicy="no-referrer" />
  return <span className="avatar avatar-initials" aria-hidden="true">{initials}</span>
}
