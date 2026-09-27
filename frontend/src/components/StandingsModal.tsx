import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { IbbaStandingRowDto } from '../api/types'

interface StandingsModalProps {
  leagueUrl: string
  leagueName: string
  highlightTeamUrl?: string
  highlightTeamName?: string
  onClose: () => void
}

export default function StandingsModal({ leagueUrl, leagueName, highlightTeamUrl, highlightTeamName, onClose }: StandingsModalProps) {
  const [rows, setRows] = useState<IbbaStandingRowDto[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    api
      .get<IbbaStandingRowDto[]>('/ibba/standings', { params: { leagueUrl } })
      .then((res) => { if (!cancelled) setRows(res.data) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [leagueUrl])

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <div className="modal-head-title">
            <div>
              <h3 dir="rtl">{leagueName}</h3>
              <p>{rows.length > 0 ? `${rows.length} teams · from IBBA` : 'From IBBA'}</p>
            </div>
          </div>
          <button className="modal-close" onClick={onClose} aria-label="Close">
            <svg className="icon"><use href="#i-x" /></svg>
          </button>
        </div>
        <div className="modal-body">
          {loading ? (
            <p style={{ padding: '1rem 1.5rem' }}>Loading...</p>
          ) : rows.length === 0 ? (
            <p style={{ padding: '1rem 1.5rem' }}>No standings synced yet.</p>
          ) : (
            <table className="standings-table">
              <thead>
                <tr>
                  <th>#</th>
                  <th className="opp">Team</th>
                  <th className="num">GP</th>
                  <th className="num">W</th>
                  <th className="num">L</th>
                  <th className="num">Pts</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => {
                  // Match by team URL when available - a substring name match (e.g.
                  // "מכבי בקה" is contained in "מכבי בקה גת") would wrongly highlight
                  // both whenever one team's name is a prefix of another's.
                  const isOwn = highlightTeamUrl ? r.teamUrl === highlightTeamUrl : highlightTeamName === r.teamName
                  return (
                  <tr key={r.teamName} className={isOwn ? 'own' : ''}>
                    <td className="num">{r.position > 0 ? r.position : '–'}</td>
                    <td className="opp" dir="ltr">
                      <span className="standings-team-cell">
                        {r.logoUrl ? <img className="standings-team-logo" src={r.logoUrl} alt="" /> : <span className="standings-team-logo-fallback" />}
                        {r.teamName}
                      </span>
                    </td>
                    <td className="num">{r.gamesPlayed}</td>
                    <td className="num">{r.wins}</td>
                    <td className="num">{r.losses}</td>
                    <td className="num">{r.leaguePoints}</td>
                  </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  )
}
