import { useEffect, useRef, useState } from 'react'
import { GoogleLogin } from '@react-oauth/google'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import type { InviteAcceptResultDto, InvitePlayerDto, InvitePreviewDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import BouncingBall, { Busy } from '../components/BouncingBall'
import { joinNamesEn, pendingInvite } from '../utils/familyInvite'

const hasGoogleClientId = Boolean(import.meta.env.VITE_GOOGLE_CLIENT_ID)

type Refusal = 'parent-account' | 'player-account' | 'already-linked' | 'used' | 'expired' | 'not-found' | 'error'

function Faces({ players }: { players: InvitePlayerDto[] }) {
  return (
    <div className="invite-faces">
      {players.slice(0, 4).map((p) =>
        p.profilePictureUrl
          ? <img key={p.id} className="invite-face" src={p.profilePictureUrl} alt="" />
          : <span key={p.id} className="invite-face">{p.firstName[0]}</span>)}
    </div>
  )
}

// The page an invite link opens (/i/{code}): who's inviting, to which kids,
// and what it gives - then sign in and you're in, no code to type.
export default function InviteLanding() {
  const { code = '' } = useParams()
  const { user, loading, loginWithGoogle, logout, refreshUser } = useAuth()
  const navigate = useNavigate()
  const [invite, setInvite] = useState<InvitePreviewDto | null>(null)
  const [missing, setMissing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [refusal, setRefusal] = useState<Refusal | null>(null)
  const [googleError, setGoogleError] = useState(false)
  const autoTried = useRef(false)

  useEffect(() => {
    api.get<InvitePreviewDto>(`/invites/${code}`)
      .then(({ data }) => setInvite(data))
      .catch(() => setMissing(true))
  }, [code])

  const accept = async () => {
    setBusy(true)
    setRefusal(null)
    try {
      const { data } = await api.post<InviteAcceptResultDto>(`/invites/${code}/accept`)
      pendingInvite.clear()
      if (data.kind === 'Player') {
        await refreshUser()
        navigate('/stats', { replace: true })
      } else {
        navigate(data.playerIds.length === 1 ? `/stats/${data.playerIds[0]}` : '/', { replace: true })
      }
    } catch (err) {
      pendingInvite.clear()
      const reason = (err as { response?: { data?: { reason?: Refusal } } })?.response?.data?.reason
      setRefusal(reason ?? 'error')
    } finally {
      setBusy(false)
    }
  }

  // Back from signing in for this invite: take it right away.
  useEffect(() => {
    if (!user || !invite || invite.status !== 'open' || autoTried.current) return
    if (pendingInvite.get()?.toUpperCase() !== code.toUpperCase()) return
    autoTried.current = true
    accept()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user, invite])

  const switchAccount = () => {
    pendingInvite.set(code)
    autoTried.current = false
    setRefusal(null)
    logout()
  }

  if (loading || (!invite && !missing)) {
    return <div className="invite-page"><BouncingBall size="lg" /></div>
  }

  const shell = (children: React.ReactNode) => (
    <div className="invite-page">
      <div className="invite-card">
        <div className="invite-brand"><svg className="icon"><use href="#i-ball" /></svg> StatsHub</div>
        {children}
      </div>
    </div>
  )

  if (missing || !invite) {
    return shell(
      <div className="invite-hero">
        <h1>This invite link doesn't work</h1>
        <p>Check that the whole link was copied, or ask for a new one.</p>
        <Link className="invite-btn ghost" to="/">Go to StatsHub</Link>
      </div>,
    )
  }

  const forPlayer = invite.kind === 'Player'
  const kid = invite.players[0]
  const names = joinNamesEn(invite.players.map((p) => p.firstName))

  if (invite.status !== 'open' && !refusal) {
    return shell(
      <div className="invite-hero">
        <Faces players={invite.players} />
        <h1>{invite.status === 'used' ? 'This link was already used' : 'This invite has expired'}</h1>
        <p>Ask {invite.invitedBy} to send you a new link.</p>
        {user && <Link className="invite-btn ghost" to="/">Back to my dashboard</Link>}
      </div>,
    )
  }

  if (refusal) {
    const accountName = user ? `${user.firstName}` : 'this account'
    const text: Record<Refusal, [string, string]> = {
      'parent-account': [`This link is for ${kid?.firstName}`, `You're signed in as ${accountName}, a parent account. ${kid?.firstName} should open it on their own phone and sign in with their own account.`],
      'player-account': ['This link is for a parent', `You're signed in as ${accountName}, a player's account. Sign in with a parent's account to join.`],
      'already-linked': ['This account already has a player', `${accountName} is already linked to a player. ${kid?.firstName} should sign in with their own account.`],
      used: ['This link was already used', `Ask ${invite.invitedBy} to send you a new link.`],
      expired: ['This invite has expired', `Ask ${invite.invitedBy} to send you a new link.`],
      'not-found': ["This invite link doesn't work", 'Ask for a new one.'],
      error: ['Something went wrong', 'Check your connection and try again.'],
    }
    const [title, body] = text[refusal]
    return shell(
      <div className="invite-hero">
        <Faces players={invite.players} />
        <h1>{title}</h1>
        <p>{body}</p>
        <div className="invite-actions">
          {refusal === 'error' && <button className="invite-btn primary" onClick={accept}>Try again</button>}
          {(refusal === 'parent-account' || refusal === 'player-account' || refusal === 'already-linked') && (
            <button className="invite-btn" onClick={switchAccount}>Switch account</button>
          )}
          <Link className="invite-btn ghost" to="/">Back to my dashboard</Link>
        </div>
      </div>,
    )
  }

  const gives = forPlayer
    ? ['Your games, points and season', 'Your player card and streaks', 'Live score when a game is on']
    : ['All their games, stats and the season', 'Record live games and edit stats', `Kids ${invite.invitedBy} adds later, too`]

  return shell(
    <>
      <div className="invite-hero">
        <Faces players={invite.players} />
        <h1>{forPlayer ? `${kid?.firstName}, this is your StatsHub page 🏀` : `${invite.invitedBy} invited you to follow ${names}`}</h1>
        <p>{forPlayer ? `${invite.invitedBy} set it up for you` : <>You're invited as a <b>parent</b></>}</p>
      </div>
      <ul className="invite-gives">
        {gives.map((g) => <li key={g}><svg className="icon"><use href="#i-check" /></svg>{g}</li>)}
      </ul>

      {user ? (
        <div className="invite-actions">
          <p className="invite-signed-in">You're signed in as {user.firstName} {user.lastName}</p>
          <button className="invite-btn primary" onClick={accept} disabled={busy}>
            {busy ? <Busy>Joining…</Busy> : forPlayer ? 'Open my page' : 'Join as a parent'}
          </button>
          <button className="invite-btn ghost" onClick={switchAccount} disabled={busy}>Not you? Switch account</button>
        </div>
      ) : (
        <div className="invite-actions">
          {hasGoogleClientId && (
            <div className="google-login-wrap">
              <GoogleLogin
                text="continue_with"
                onSuccess={async (res) => {
                  if (!res.credential) return
                  setGoogleError(false)
                  pendingInvite.set(code)
                  try {
                    await loginWithGoogle(res.credential)
                  } catch {
                    setGoogleError(true)
                  }
                }}
                onError={() => setGoogleError(true)}
              />
            </div>
          )}
          {googleError && <p className="error">Google sign-in failed. Please try again.</p>}
          {hasGoogleClientId && <div className="auth-divider"><span>or</span></div>}
          <button className="invite-btn" onClick={() => { pendingInvite.set(code); navigate(`/login?invite=${code}&mode=register`) }}>
            Sign up with email
          </button>
          <p className="invite-signed-in">
            Already have an account?{' '}
            <button className="invite-link" onClick={() => { pendingInvite.set(code); navigate(`/login?invite=${code}`) }}>Sign in</button>
          </p>
        </div>
      )}
    </>,
  )
}
