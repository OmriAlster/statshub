import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { FamilyInviteDto, FamilyInviteKind, PlayerDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { Busy } from './BouncingBall'
import Modal from './Modal'
import { inviteMessage, inviteUrl, whatsAppHref } from '../utils/familyInvite'

function Face({ player }: { player: PlayerDto }) {
  return player.profilePictureUrl
    ? <img className="invite-pick-face" src={player.profilePictureUrl} alt="" />
    : <span className="invite-pick-face">{player.firstName[0]}</span>
}

// "Invite to the family": a parent or family member (any of your kids, used
// by as many people as you send it to), or one of your kids' own login.
export default function InviteFamilyModal({ players, onClose, onCreated }: {
  players: PlayerDto[]
  onClose: () => void
  onCreated: (invite: FamilyInviteDto) => void
}) {
  const { user } = useAuth()
  const [kind, setKind] = useState<FamilyInviteKind>('Parent')
  const [kids, setKids] = useState<string[]>(players.map((p) => p.id))
  const firstWithoutLogin = players.find((p) => !p.linkedUser)
  const [kid, setKid] = useState<string | null>(firstWithoutLogin?.id ?? null)
  const [invite, setInvite] = useState<FamilyInviteDto | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  // The link appears at the bottom - bring it into view on a short screen.
  const sendRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (invite) sendRef.current?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }, [invite])

  const pickKind = (k: FamilyInviteKind) => {
    setKind(k)
    setInvite(null)
    setError(null)
  }

  const toggleKid = (id: string) => {
    setInvite(null)
    setKids((prev) => (prev.includes(id) ? prev.filter((k) => k !== id) : [...prev, id]))
  }

  const chosen = kind === 'Parent' ? kids : kid ? [kid] : []

  const create = async () => {
    setBusy(true)
    setError(null)
    try {
      const { data } = await api.post<FamilyInviteDto>('/invites', { kind, playerIds: chosen })
      setInvite(data)
      onCreated(data)
    } catch {
      setError('Could not create the link. Try again.')
    } finally {
      setBusy(false)
    }
  }

  const message = invite ? inviteMessage(invite, user?.firstName ?? '') : ''

  const copy = async () => {
    if (!invite) return
    try {
      await navigator.clipboard.writeText(inviteUrl(invite.code))
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      setError('Copy the link above by hand.')
    }
  }

  const share = async () => {
    try {
      await navigator.share({ text: message })
    } catch { /* closed the share sheet */ }
  }

  const canShare = typeof navigator !== 'undefined' && 'share' in navigator

  return (
    <Modal head={<h3>Invite to the family</h3>} onClose={onClose} panelClassName="invite-modal" label="Invite to the family">
      <div className="invite-modal-body">
        <div className="invite-kinds">
          <button type="button" className={`invite-kind ${kind === 'Parent' ? 'on' : ''}`} onClick={() => pickKind('Parent')}>
            <span className="invite-kind-icon" aria-hidden="true">👪</span>
            <span><b>A parent or family member</b><small>Sees and edits your kids' games and stats</small></span>
          </button>
          <button type="button" className={`invite-kind ${kind === 'Player' ? 'on' : ''}`} onClick={() => pickKind('Player')}>
            <span className="invite-kind-icon" aria-hidden="true">🏀</span>
            <span><b>My child</b><small>Their own login, to see their own page</small></span>
          </button>
        </div>

        <p className="invite-label">{kind === 'Parent' ? 'Which kids?' : 'Who is it for?'}</p>
        <div className="invite-picks">
          {players.map((p) => {
            if (kind === 'Parent') {
              const on = kids.includes(p.id)
              return (
                <label key={p.id} className="invite-pick">
                  <Face player={p} />
                  <span>{p.firstName} {p.lastName}</span>
                  <input type="checkbox" checked={on} onChange={() => toggleKid(p.id)} />
                </label>
              )
            }
            const joined = !!p.linkedUser
            return (
              <label key={p.id} className={`invite-pick ${joined ? 'done' : ''}`}>
                <Face player={p} />
                <span>{p.firstName} {p.lastName}</span>
                {joined
                  ? <small>already joined</small>
                  : <input type="radio" name="invite-kid" checked={kid === p.id} onChange={() => { setKid(p.id); setInvite(null) }} />}
              </label>
            )
          })}
        </div>

        {kind === 'Parent'
          ? <p className="invite-note">Anyone you send it to can join, for 7 days. Kids you add later are shared with them too.</p>
          : <p className="invite-note">One kid, one account. The link works once, for 7 days.</p>}

        {error && <p className="error">{error}</p>}

        {invite ? (
          <div className="invite-send" ref={sendRef}>
            <code className="invite-url">{inviteUrl(invite.code)}</code>
            <a className="invite-btn wa" href={whatsAppHref(message)} target="_blank" rel="noopener noreferrer">
              Send on WhatsApp
            </a>
            <div className="invite-send-row">
              <button type="button" className="invite-btn" onClick={copy}>{copied ? 'Copied ✓' : 'Copy link'}</button>
              {canShare && <button type="button" className="invite-btn" onClick={share}>More…</button>}
            </div>
          </div>
        ) : (
          <button type="button" className="invite-btn primary" onClick={create} disabled={busy || chosen.length === 0}>
            {busy ? <Busy>Creating…</Busy> : 'Create the link'}
          </button>
        )}
      </div>
    </Modal>
  )
}
