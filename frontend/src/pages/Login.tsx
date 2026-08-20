import { useState } from 'react'
import { GoogleLogin } from '@react-oauth/google'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

const devLoginEnabled = import.meta.env.DEV
const hasGoogleClientId = Boolean(import.meta.env.VITE_GOOGLE_CLIENT_ID)

export default function Login() {
  const { user, loading, loginWithGoogle, loginWithPassword, register, devLogin } = useAuth()
  const [error, setError] = useState<string | null>(null)

  const [mode, setMode] = useState<'signin' | 'register'>('signin')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [busy, setBusy] = useState(false)

  const [devEmail, setDevEmail] = useState('')
  const [devName, setDevName] = useState('')
  const [devBusy, setDevBusy] = useState(false)

  if (!loading && user) {
    return <Navigate to="/" replace />
  }

  const handleSubmit = async () => {
    if (!email.trim() || !password) {
      setError('Enter an email and password')
      return
    }
    if (mode === 'register' && password.length < 6) {
      setError('Password must be at least 6 characters')
      return
    }
    setBusy(true)
    setError(null)
    try {
      if (mode === 'signin') {
        await loginWithPassword(email.trim(), password)
      } else {
        await register(email.trim(), password, firstName.trim(), lastName.trim())
      }
    } catch (err) {
      const message =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        (mode === 'signin' ? 'Invalid email or password.' : 'Could not create your account.')
      setError(message)
    } finally {
      setBusy(false)
    }
  }

  const handleDevLogin = async () => {
    if (!devEmail) {
      setError('Enter an email to continue')
      return
    }
    setDevBusy(true)
    setError(null)
    try {
      const [devFirstName, ...rest] = devName.trim().split(' ')
      await devLogin(devEmail, devFirstName || 'Coach', rest.join(' '))
    } catch {
      setError('Dev login failed - is the backend running?')
    } finally {
      setDevBusy(false)
    }
  }

  return (
    <div className="login-container">
      <div className="login-brand-half">
        <div className="brand-mark login-brand-mark">
          <svg className="icon" style={{ width: 26, height: 26, stroke: '#2b1400' }}><use href="#i-ball" /></svg>
        </div>
        <h1>Every rebound,
        every quarter, saved.</h1>
        <p>Tap stats live courtside, watch season averages update automatically, and share a read-only link with family in one click.</p>
        <ul className="login-brand-list">
          <li><svg className="icon"><use href="#i-check" /></svg>Free throws, boards, assists, steals, blocks &amp; fouls</li>
          <li><svg className="icon"><use href="#i-check" /></svg>Kid logins with read-only stats</li>
          <li><svg className="icon"><use href="#i-check" /></svg>Live share links, no login required</li>
        </ul>
      </div>

      <div className="login-box">
        <h2><svg className="icon" style={{ width: 22, height: 22 }}><use href="#i-ball" /></svg> StatsHub</h2>
        <p>Basketball Game Stats Tracker for Parents &amp; Players</p>

        <div className="login-content">
          <p className="login-mobile-only">Track your child's basketball statistics live, courtside, and share game highlights with family.</p>

          <div className="password-auth-box">
            <div className="auth-mode-toggle">
              <button type="button" className={`toggle-option ${mode === 'signin' ? 'active' : ''}`} onClick={() => setMode('signin')}>
                Sign In
              </button>
              <button type="button" className={`toggle-option ${mode === 'register' ? 'active' : ''}`} onClick={() => setMode('register')}>
                Create Account
              </button>
            </div>

            <div className="setup-form">
              {mode === 'register' && (
                <div className="form-row" style={{ marginBottom: 0 }}>
                  <label>
                    First Name
                    <input value={firstName} onChange={(e) => setFirstName(e.target.value)} />
                  </label>
                  <label>
                    Last Name
                    <input value={lastName} onChange={(e) => setLastName(e.target.value)} />
                  </label>
                </div>
              )}
              <div>
                <label>Email:</label>
                <input type="email" placeholder="you@example.com" value={email} onChange={(e) => setEmail(e.target.value)} />
              </div>
              <div>
                <label>Password:</label>
                <input
                  type="password"
                  placeholder={mode === 'register' ? 'At least 6 characters' : 'Your password'}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </div>
            </div>
            <button className="submit-btn" onClick={handleSubmit} disabled={busy}>
              {busy ? 'Please wait...' : mode === 'signin' ? 'Sign In' : 'Create Account'}
            </button>
          </div>

          {hasGoogleClientId && (
            <>
              <div className="auth-divider"><span>or</span></div>
              <div className="google-login-wrap">
                <GoogleLogin
                  onSuccess={async (credentialResponse) => {
                    if (!credentialResponse.credential) return
                    setError(null)
                    try {
                      await loginWithGoogle(credentialResponse.credential)
                    } catch {
                      setError('Google sign-in failed. Please try again.')
                    }
                  }}
                  onError={() => setError('Google sign-in failed. Please try again.')}
                />
              </div>
            </>
          )}

          {error && <p className="error">{error}</p>}

          {devLoginEnabled && (
            <div className="dev-login-box">
              <h4>Local dev login</h4>
              <p>Skip auth while you're building - only available in development mode.</p>
              <input
                type="email"
                placeholder="you@example.com"
                value={devEmail}
                onChange={(e) => setDevEmail(e.target.value)}
              />
              <input
                type="text"
                placeholder="Full name (optional)"
                value={devName}
                onChange={(e) => setDevName(e.target.value)}
              />
              <button className="submit-btn" onClick={handleDevLogin} disabled={devBusy}>
                {devBusy ? 'Signing in...' : 'Continue with dev login'}
              </button>
            </div>
          )}

          <div className="features login-mobile-only">
            <h3>Features:</h3>
            <ul>
              <li>✅ Record live game stats: 3PT, 2PT, assists, and more</li>
              <li>✅ Track season progress automatically</li>
              <li>✅ Share game and season stats via a link</li>
              <li>✅ Manage multiple players from one account</li>
              <li>✅ Players can sign in and see their own stats</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  )
}
