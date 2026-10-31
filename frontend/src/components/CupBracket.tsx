import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { GameDto } from '../api/types'
import BouncingBall from './BouncingBall'
import Modal from './Modal'

interface CupSide { name: string; teamUrl: string; logoUrl?: string | null; score?: number | null; won: boolean }
interface CupGame { home?: CupSide | null; away?: CupSide | null; localDate?: string | null }
interface Cup { name: string; pageUrl: string; rounds: { name: string; games: CupGame[] }[] }

// The cups the team plays in this season, as brackets (IBBA's), the team lit
// up along its path. A cup whose bracket isn't published yet shows the
// team's own cup games instead.
export default function CupBracket({ teamUrl, league, teamName, cupGames, playerId, onClose }: {
  teamUrl: string
  league?: string | null
  teamName: string
  cupGames: GameDto[]
  playerId: string
  onClose: () => void
}) {
  const [cups, setCups] = useState<Cup[] | null>(null)

  useEffect(() => {
    let current = true
    api
      .get<Cup[]>('/ibba/cups', { params: { teamUrl, league: league ?? undefined } })
      .then((res) => current && setCups(res.data))
      .catch(() => current && setCups([]))
    return () => { current = false }
  }, [teamUrl, league])

  const drawn = (cups ?? []).filter((c) => c.rounds.length > 0)
  const isUs = (side?: CupSide | null) => !!side && side.teamUrl === teamUrl

  return (
    <Modal kind="editor" head={<h3>🏆 Cup</h3>} onClose={onClose} label="Cup bracket">
      <div className="edit-panel cup-panel">
      {cups === null && <BouncingBall size="sm" label="Loading the bracket…" />}

      {drawn.map((cup) => {
        // Where the team is now: its latest game in the bracket.
        const ourGames = cup.rounds.flatMap((r, ri) => r.games.map((g) => ({ g, ri, round: r.name }))).filter(({ g }) => isUs(g.home) || isUs(g.away))
        const now = ourGames[ourGames.length - 1]
        const out = now && (isUs(now.g.home) ? now.g.away?.won : now.g.home?.won)
        return (
          <section key={cup.pageUrl} className="cup-section">
            <div className="cup-head">
              <h4 dir="auto">{cup.name}</h4>
              {now && (
                <span className={`cup-status ${out ? 'out' : ''}`}>
                  {out ? `Out in the ${now.round.toLowerCase()}` : `${now.round}${now.g.localDate ? ` · ${new Date(now.g.localDate).toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' })}` : ''}`}
                </span>
              )}
            </div>
            <div className="cup-bracket" role="list" aria-label={`${cup.name} bracket`}>
              {cup.rounds.map((round) => (
                <div className="cup-round" key={round.name} role="listitem">
                  <h5>{round.name}</h5>
                  {round.games.map((game, i) => (
                    <div key={i} className={`cup-game ${isUs(game.home) || isUs(game.away) ? 'ours' : ''}`}>
                      {[game.home, game.away].map((side, s) => (
                        <div key={s} className={`cup-side ${side?.won ? 'won' : ''} ${side && game.home?.score != null && !side.won ? 'lost' : ''} ${isUs(side) ? 'us' : ''}`}>
                          {side?.logoUrl ? <img src={side.logoUrl} alt="" /> : <span className="cup-crest-blank" />}
                          <span className="cup-name" dir="auto">{side?.name ?? 'TBD'}</span>
                          <b>{side?.score ?? ''}</b>
                        </div>
                      ))}
                    </div>
                  ))}
                </div>
              ))}
            </div>
            <a className="cup-source" href={cup.pageUrl} target="_blank" rel="noopener noreferrer">The full cup on IBBA ↗</a>
          </section>
        )
      })}

      {cups !== null && drawn.length === 0 && (
        <div className="cup-empty">
          <p>IBBA hasn't published {teamName}&apos;s cup bracket yet.</p>
          {cupGames.length > 0 && (
            <ul className="matchup-games">
              {cupGames.map((g) => (
                <li key={g.id}>
                  <Link to={`/games/${g.id}?playerId=${playerId}`} onClick={onClose}>
                    <span className="matchup-date">{new Date(g.gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}</span>
                    <span dir="auto">vs {g.opponentName}</span>
                    <span className={`matchup-result ${g.status !== 'Completed' ? '' : (g.teamScore ?? 0) > (g.opponentScore ?? 0) ? 'win' : 'loss'}`}>
                      {g.status === 'Completed' ? `${(g.teamScore ?? 0) > (g.opponentScore ?? 0) ? 'W' : 'L'} ${g.teamScore}-${g.opponentScore}` : 'Upcoming'}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
      </div>
    </Modal>
  )
}
