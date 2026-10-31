import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

interface MatchupTeam {
  name: string
  logoUrl?: string | null
  leagueName?: string | null
  position: number
  totalTeams?: number | null
  gamesPlayed: number
  wins: number
  losses: number
  pointsFor: number
  pointsAgainst: number
}

interface HeadToHeadGame {
  gameId: string
  gameDate: string
  gameType: string
  isHomeGame?: boolean | null
  teamScore?: number | null
  opponentScore?: number | null
  points?: number | null
  rebounds?: number | null
  assists?: number | null
}

interface Matchup {
  opponent?: MatchupTeam | null
  team?: MatchupTeam | null
  previous: HeadToHeadGame[]
}

const perGame = (total: number, games: number) => (games > 0 ? Math.round(total / games) : 0)

// Before a game: the opponent in this season's league table next to us, and
// this season's earlier meetings with the player's line in each.
export default function MatchupCard({ gameId, playerId, opponentName, showScouting }: { gameId: string; playerId: string; opponentName: string; showScouting: boolean }) {
  const [matchup, setMatchup] = useState<Matchup | null>(null)

  useEffect(() => {
    let current = true
    api
      .get<Matchup>(`/games/${gameId}/matchup`, { params: { playerId } })
      .then((res) => current && setMatchup(res.data))
      .catch(() => current && setMatchup(null))
    return () => { current = false }
  }, [gameId, playerId])

  if (!matchup) return null
  const scouting = showScouting ? matchup.opponent : null
  if (!scouting && matchup.previous.length === 0) return null

  const wins = matchup.previous.filter((g) => (g.teamScore ?? 0) > (g.opponentScore ?? 0)).length
  const withLine = matchup.previous.filter((g) => g.points != null)
  const avg = (pick: (g: HeadToHeadGame) => number) =>
    withLine.length ? (withLine.reduce((s, g) => s + pick(g), 0) / withLine.length).toFixed(1) : null

  return (
    <div className="matchup">
      {scouting && (
        <section className="matchup-card" aria-labelledby="scouting-title">
          <div className="matchup-head">
            <h3 id="scouting-title">Opponent</h3>
            <span className="matchup-place">{scouting.position}{scouting.totalTeams ? ` of ${scouting.totalTeams}` : ''}</span>
          </div>
          <div className="matchup-team">
            {scouting.logoUrl && <img src={scouting.logoUrl} alt="" />}
            <span dir="auto">{opponentName || scouting.name}</span>
          </div>
          <div className="matchup-numbers">
            <div><b>{scouting.wins}-{scouting.losses}</b><span>Record</span></div>
            <div><b>{perGame(scouting.pointsFor, scouting.gamesPlayed)}</b><span>Score / game</span></div>
            <div><b>{perGame(scouting.pointsAgainst, scouting.gamesPlayed)}</b><span>Allow / game</span></div>
          </div>
          {matchup.team && (
            <p className="matchup-us">
              You: {matchup.team.position}{matchup.team.totalTeams ? ` of ${matchup.team.totalTeams}` : ''} · {matchup.team.wins}-{matchup.team.losses} ·{' '}
              {perGame(matchup.team.pointsFor, matchup.team.gamesPlayed)} / {perGame(matchup.team.pointsAgainst, matchup.team.gamesPlayed)} per game
            </p>
          )}
        </section>
      )}

      {matchup.previous.length > 0 && (
        <section className="matchup-card" aria-labelledby="h2h-title">
          <div className="matchup-head">
            <h3 id="h2h-title">Earlier this season</h3>
            <span className={`matchup-record ${wins * 2 >= matchup.previous.length ? 'good' : 'bad'}`}>{wins}-{matchup.previous.length - wins}</span>
          </div>
          <ul className="matchup-games">
            {matchup.previous.map((g) => {
              const won = (g.teamScore ?? 0) > (g.opponentScore ?? 0)
              return (
                <li key={g.gameId}>
                  <Link to={`/games/${g.gameId}?playerId=${playerId}`}>
                    <span className="matchup-date">
                      {new Date(g.gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}{' '}
                      {g.isHomeGame == null ? '' : g.isHomeGame ? '🏠' : '✈️'}
                    </span>
                    <span className={`matchup-result ${won ? 'win' : 'loss'}`}>{won ? 'W' : 'L'} {g.teamScore}-{g.opponentScore}</span>
                    <span className="matchup-line">{g.points != null ? `${g.points} pts · ${g.rebounds} reb · ${g.assists} ast` : 'No box score'}</span>
                  </Link>
                </li>
              )
            })}
          </ul>
          {withLine.length > 1 && (
            <p className="matchup-us">Against them: {avg((g) => g.points ?? 0)} pts · {avg((g) => g.rebounds ?? 0)} reb · {avg((g) => g.assists ?? 0)} ast per game</p>
          )}
        </section>
      )}
    </div>
  )
}
