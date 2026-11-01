import { useState } from 'react'
import { api } from '../api/client'
import type { IbbaPreviewDto } from '../api/types'
import { Busy } from '../components/BouncingBall'

interface Hit { leagueName: string; playerName: string; playerUrl: string }

interface LabGame {
  date: string
  opponent: string
  home: boolean
  teamScore: number | null
  opponentScore: number | null
  status: 'found' | 'not-on-sheet' | 'ambiguous' | 'no-sheet' | 'invalid-sheet'
  note: string | null
  number: number | null
  code: string | null
  nameOnSheet: string | null
  played: boolean
  starter: boolean
  captain: boolean
  points: number
  freeThrows: number
  twoPointers: number
  threePointers: number
  fouls: number
}

interface LabResult { league: string; team: string; numbers: number[]; games: LabGame[]; upcomingGames: number }

type TeamState = { busy: boolean; mode?: 'games' | 'jersey'; result?: LabResult; error?: string }

const statusText: Record<LabGame['status'], string> = {
  found: '',
  'not-on-sheet': 'Not on this sheet',
  ambiguous: 'More than one player fits',
  'no-sheet': 'No scoresheet',
  'invalid-sheet': "Sheet doesn't add up",
}

const errorOf = (err: unknown, fallback: string) =>
  (err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

// Trial page: an IBBA player, then their numbers read off the league's game scoresheets.
export default function ScoresheetLab() {
  const [name, setName] = useState('')
  const [searching, setSearching] = useState(false)
  const [hits, setHits] = useState<Hit[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState<IbbaPreviewDto | null>(null)
  const [opening, setOpening] = useState<string | null>(null)
  const [teams, setTeams] = useState<Record<string, TeamState>>({})

  const search = async (e: React.FormEvent) => {
    e.preventDefault()
    if (name.trim().length < 2) return
    setSearching(true)
    setError(null)
    setPreview(null)
    setTeams({})
    try {
      const { data } = await api.get<Hit[]>('/ibba/search', { params: { name: name.trim() } })
      setHits(data)
    } catch (err) {
      setError(errorOf(err, 'The search failed.'))
    } finally {
      setSearching(false)
    }
  }

  const open = async (hit: Hit) => {
    setOpening(hit.playerUrl)
    setError(null)
    setTeams({})
    try {
      const { data } = await api.get<IbbaPreviewDto>('/ibba/preview', { params: { playerUrl: hit.playerUrl } })
      setPreview(data)
    } catch (err) {
      setError(errorOf(err, "Could not open this player's page."))
    } finally {
      setOpening(null)
    }
  }

  // Keyed by the team's page: one player can have two teams with the same name (two age groups).
  const read = async (key: string, teamName: string, leagueName: string | null | undefined, mode: 'games' | 'jersey') => {
    if (!preview) return
    setTeams((prev) => ({ ...prev, [key]: { busy: true, mode } }))
    try {
      const { data } = await api.get<LabResult>('/scoresheet-lab', {
        params: { playerName: preview.playerName, teamName, leagueName: leagueName ?? undefined, mode },
      })
      setTeams((prev) => ({ ...prev, [key]: { busy: false, mode, result: data } }))
    } catch (err) {
      setTeams((prev) => ({ ...prev, [key]: { busy: false, mode, error: errorOf(err, 'Reading the scoresheets failed.') } }))
    }
  }

  return (
    <div className="page-container lab">
      <h2>🧾 Scoresheet lab</h2>
      <p className="lab-sub">Find a player on IBBA, then read their games off the league's scoresheets (Swish).</p>

      <form className="lab-search" onSubmit={search}>
        <input id="lab-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="Player name in Hebrew" dir="auto" />
        <button type="submit" className="lab-btn primary" disabled={searching}>{searching ? <Busy>Searching…</Busy> : 'Search'}</button>
      </form>
      {error && <p className="error">{error}</p>}

      {hits && !preview && (
        <div className="lab-hits">
          {hits.length === 0 && <p className="lab-sub">No players found.</p>}
          {hits.map((hit) => (
            <button key={hit.playerUrl} type="button" className="lab-hit" onClick={() => open(hit)} disabled={!!opening}>
              <b dir="auto">{hit.playerName}</b>
              <span>{opening === hit.playerUrl ? 'Opening…' : hit.leagueName}</span>
            </button>
          ))}
        </div>
      )}

      {preview && (
        <div className="lab-player">
          <div className="lab-player-head">
            {preview.photoUrl && <img src={preview.photoUrl} alt="" />}
            <div>
              <b dir="auto">{preview.playerName}</b>
              <button type="button" className="lab-link" onClick={() => setPreview(null)}>Back to results</button>
            </div>
          </div>

          {preview.teams.map((team) => {
            const state = teams[team.teamUrl]
            const result = state?.result
            return (
              <section key={team.teamUrl} className="lab-team">
                <div className="lab-team-head">
                  <div>
                    <b dir="auto">{team.teamName}</b>
                    <span dir="auto">{team.leagueName ?? 'League not known yet'}</span>
                  </div>
                  <div className="lab-team-actions">
                    <button type="button" className="lab-btn" disabled={state?.busy} onClick={() => read(team.teamUrl, team.teamName, team.leagueName, 'jersey')}>
                      {state?.busy && state.mode === 'jersey' ? <Busy>Reading…</Busy> : 'Find jersey number'}
                    </button>
                    <button type="button" className="lab-btn primary" disabled={state?.busy} onClick={() => read(team.teamUrl, team.teamName, team.leagueName, 'games')}>
                      {state?.busy && state.mode === 'games' ? <Busy>Reading…</Busy> : 'All games'}
                    </button>
                  </div>
                </div>

                {state?.error && <p className="error">{state.error}</p>}
                {result && (
                  <div className="lab-result">
                    <p className="lab-number">
                      {result.numbers.length > 0
                        ? <>Jersey <b>#{result.numbers.join(' / #')}</b></>
                        : 'Not found on any sheet yet'}
                      <span className="lab-source" dir="auto">On Swish: {result.team} · {result.league}</span>
                    </p>
                    {state.mode === 'games' && (
                      <div className="lab-table-wrap">
                        <table className="lab-table">
                          <thead>
                            <tr><th>Date</th><th>Game</th><th>#</th><th>PTS</th><th>FT</th><th>2PT</th><th>3PT</th><th>Fouls</th><th></th></tr>
                          </thead>
                          <tbody>
                            {result.games.map((g) => (
                              <tr key={g.date + g.opponent} className={g.status === 'found' ? '' : 'muted'}>
                                <td>{new Date(g.date).toLocaleDateString('en-GB', { day: '2-digit', month: '2-digit', year: '2-digit' })}</td>
                                <td dir="auto">{g.home ? 'vs' : '@'} {g.opponent} <span className="lab-score">{g.teamScore}–{g.opponentScore}</span></td>
                                <td>{g.number ?? '–'}</td>
                                <td><b>{g.status === 'found' ? g.points : '–'}</b></td>
                                <td>{g.status === 'found' ? g.freeThrows : '–'}</td>
                                <td>{g.status === 'found' ? g.twoPointers : '–'}</td>
                                <td>{g.status === 'found' ? g.threePointers : '–'}</td>
                                <td>{g.status === 'found' ? g.fouls : '–'}</td>
                                <td className="lab-flags">
                                  {g.status === 'found'
                                    ? [g.starter && 'Starter', g.captain && 'Captain', !g.played && "Didn't play"].filter(Boolean).join(' · ')
                                    : statusText[g.status]}
                                  {g.note && <span className="lab-note" dir="auto"> {g.note}</span>}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                        {result.games.length === 0 && <p className="lab-sub">No finished games yet.</p>}
                      </div>
                    )}
                  </div>
                )}
              </section>
            )
          })}
        </div>
      )}
    </div>
  )
}
