import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'

export default function JoinAsPlayer() {
  const { user, refreshUser } = useAuth()
  const navigate = useNavigate()
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [parentCode, setParentCode] = useState('')
  const [parentBusy, setParentBusy] = useState(false)
  const [parentError, setParentError] = useState<string | null>(null)
  const [parentSuccess, setParentSuccess] = useState(false)

  const claim = async () => {
    if (!code.trim()) return
    setBusy(true)
    setError(null)
    try {
      await api.post('/players/claim-invite', { inviteCode: code.trim() })
      await refreshUser()
      navigate('/')
    } catch {
      setError('That code is invalid or expired. Ask for a new one.')
    } finally {
      setBusy(false)
    }
  }

  const claimAsParent = async () => {
    if (!parentCode.trim()) return
    setParentBusy(true)
    setParentError(null)
    try {
      await api.post('/players/claim-parent-invite', { inviteCode: parentCode.trim() })
      setParentSuccess(true)
      setParentCode('')
    } catch {
      setParentError('That code is invalid or expired. Ask for a new one.')
    } finally {
      setParentBusy(false)
    }
  }

  if (user?.role === 'Player' && user.linkedPlayer) {
    return (
      <div className="page-container">
        <h2>🧒 Join as a Player</h2>
        <p>You're already linked to {user.linkedPlayer.firstName} {user.linkedPlayer.lastName}'s profile.</p>
      </div>
    )
  }

  return (
    <div className="page-container">
      <h2>🧒 Join as a Player</h2>
      <p>Ask your parent or coach for your invite code, then enter it below to see your own stats.</p>
      <div className="setup-form" style={{ maxWidth: 320 }}>
        <div>
          <label>Invite Code:</label>
          <input value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} placeholder="e.g. AB3D9F2K" />
        </div>
      </div>
      {error && <p className="error">{error}</p>}
      <button className="submit-btn" onClick={claim} disabled={busy}>
        {busy ? 'Joining...' : 'Join'}
      </button>

      <div className="form-section" style={{ marginTop: '2rem' }}>
        <h3>👪 Join as a Second Parent</h3>
        <p>If your co-parent already tracks this player, ask them for a parent invite code from the Players page.</p>
        <div className="setup-form" style={{ maxWidth: 320 }}>
          <div>
            <label>Invite Code:</label>
            <input value={parentCode} onChange={(e) => setParentCode(e.target.value.toUpperCase())} placeholder="e.g. CD7K2M9P" />
          </div>
        </div>
        {parentError && <p className="error">{parentError}</p>}
        {parentSuccess && <p>You now have access to this player's stats and games. Go to the Dashboard to see them.</p>}
        <button className="submit-btn" onClick={claimAsParent} disabled={parentBusy}>
          {parentBusy ? 'Joining...' : 'Join as Parent'}
        </button>
      </div>
    </div>
  )
}
