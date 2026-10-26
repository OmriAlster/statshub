import type { ReactNode } from 'react'
import type { IbbaPreviewDto } from '../api/types'
import { Busy } from './BouncingBall'
import LeagueBadge from './LeagueBadge'
import TeamCrest from './TeamCrest'

// What a pasted league link turned out to be, before anything is linked or
// created: the player (photo, name, birth date - checked against the one in
// StatsHub when there is one) and their current teams, then a clear yes / no.
export default function LeagueFoundCard({
  preview,
  appDateOfBirth,
  appFirstName,
  confirmLabel,
  busy,
  busyLabel,
  canConfirm = true,
  onConfirm,
  onBack,
  children,
}: {
  preview: IbbaPreviewDto
  appDateOfBirth?: string | null
  appFirstName?: string
  confirmLabel: string
  busy: boolean
  busyLabel: string
  canConfirm?: boolean
  onConfirm: () => void
  onBack: () => void
  children?: ReactNode
}) {
  const born = preview.dateOfBirth ? new Date(preview.dateOfBirth) : null
  const sameDay = (a: Date, b: Date) => a.getUTCFullYear() === b.getUTCFullYear() && a.getUTCMonth() === b.getUTCMonth() && a.getUTCDate() === b.getUTCDate()
  const appBorn = appDateOfBirth ? new Date(appDateOfBirth) : null
  const birthCheck = born && appBorn ? (sameDay(born, appBorn) ? 'match' : 'differs') : null
  const initials = preview.playerName.split(' ').filter(Boolean).map((p) => p[0]).slice(0, 2).join('')

  return (
    <div className="found-card">
      <div className="found-who">
      <p className="found-status">
        <svg className="icon" aria-hidden="true"><use href="#i-check" /></svg>
        Found on {preview.leagueName}
      </p>

      <div className="found-player">
        {preview.photoUrl ? (
          <img className="found-avatar" src={preview.photoUrl} alt="" />
        ) : (
          <span className="found-avatar">{initials}</span>
        )}
        <div className="found-player-text">
          <span className="found-name-row">
            <span className="found-name" dir="auto">{preview.playerName}</span>
            <LeagueBadge league={preview.leagueKey} />
          </span>
          {born && (
            <span className="found-born">
              Born {born.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })}
              {birthCheck === 'match' && <span className="found-ok"> · matches {appFirstName ?? 'the player'}&apos;s birth date</span>}
              {birthCheck === 'differs' && <span className="found-warn"> · not the birth date in StatsHub - check it&apos;s the right player</span>}
            </span>
          )}
        </div>
      </div>

      </div>

      <div className="found-details">
      <p className="found-label">Current teams on {preview.leagueName}</p>
      <ul className="found-teams">
        {preview.teams.map((t) => (
          <li key={t.teamUrl || t.teamName} className="found-team">
            <TeamCrest logoUrl={t.logoUrl} league={preview.leagueKey} size="sm" />
            <span className="found-team-text">
              <span className="found-team-name" dir="auto">{t.teamName}</span>
              <span className="found-team-meta">
                {/* <bdi> keeps a Hebrew league name from flipping the English beside it */}
                {t.leagueName && <><bdi>{t.leagueName}</bdi> · </>}
                {t.isAdditional ? 'also allowed to play' : 'main team'}
              </span>
            </span>
            {t.position != null && t.totalTeams != null && (
              <span className="found-position">{t.position}{ordinal(t.position)} of {t.totalTeams}</span>
            )}
          </li>
        ))}
      </ul>

      {children}

      <div className="found-actions">
        {canConfirm && (
          <button className="found-confirm" onClick={onConfirm} disabled={busy}>
            {busy ? <Busy>{busyLabel}</Busy> : confirmLabel}
          </button>
        )}
        <button className="found-back" onClick={onBack} disabled={busy}>No, use another link</button>
      </div>
      </div>
    </div>
  )
}

function ordinal(n: number) {
  const tens = n % 100
  if (tens >= 11 && tens <= 13) return 'th'
  return n % 10 === 1 ? 'st' : n % 10 === 2 ? 'nd' : n % 10 === 3 ? 'rd' : 'th'
}
