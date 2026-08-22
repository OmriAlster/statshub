import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { GameDto, IbbaLinkStatusDto, PlayerDto, PlayerTeamStatsDto, SeasonDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import IbbaBadge from '../components/IbbaBadge'
import StandingsModal from '../components/StandingsModal'
import TeamCrest from '../components/TeamCrest'

interface PlayerCard {
  player: PlayerDto
  teamStats: PlayerTeamStatsDto[]
  ibba: IbbaLinkStatusDto | null
  gamesByTeam: Record<number, GameDto[]>
}

function lastAndNextGame(games: GameDto[]) {
  const completed = games.filter((g) => g.status === 'Completed').sort((a, b) => new Date(b.gameDate).getTime() - new Date(a.gameDate).getTime())
  const upcoming = games.filter((g) => g.status === 'Upcoming').sort((a, b) => new Date(a.gameDate).getTime() - new Date(b.gameDate).getTime())
  return { last: completed[0] ?? null, next: upcoming[0] ?? null }
}

export default function Dashboard() {
  const { user } = useAuth()
  const isPlayerRole = user?.role === 'Player'
  const [players, setPlayers] = useState<PlayerCard[]>([])
  const [currentSeason, setCurrentSeason] = useState<SeasonDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [standingsFor, setStandingsFor] = useState<{ leagueUrl: string; leagueName: string; teamName: string } | null>(null)

  useEffect(() => {
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isPlayerRole])

  const load = async () => {
    try {
      setLoading(true)

      const playersPromise: Promise<PlayerDto[]> =
        isPlayerRole && user?.linkedPlayer
          ? Promise.resolve([user.linkedPlayer])
          : api.get<PlayerDto[]>('/players').then((res) => res.data)

      const seasonPromise: Promise<SeasonDto | null> = isPlayerRole
        ? Promise.resolve(null)
        : api.get<SeasonDto[]>('/seasons').then((res) => res.data[0] ?? null)

      const [basePlayers, season] = await Promise.all([playersPromise, seasonPromise])
      setCurrentSeason(season)

      const cardsPromise = Promise.all(
        basePlayers.map(async (player): Promise<PlayerCard> => {
          const [teamStats, games, ibba] = await Promise.all([
            api.get<PlayerTeamStatsDto[]>(`/gamestats/player/${player.id}`).then((res) => res.data).catch(() => []),
            api.get<GameDto[]>(`/games/player/${player.id}`).then((res) => res.data).catch(() => []),
            api.get<IbbaLinkStatusDto>(`/players/${player.id}/ibba`).then((res) => res.data).catch(() => null),
          ])

          const gamesByTeam: Record<number, GameDto[]> = {}
          for (const g of games) {
            ;(gamesByTeam[g.teamId] ??= []).push(g)
          }

          return { player, teamStats, ibba, gamesByTeam }
        })
      )

      const cards = await cardsPromise
      setPlayers(cards)

      setError(null)
    } catch {
      setError('Could not load your dashboard. Is the backend running?')
    } finally {
      setLoading(false)
    }
  }

  if (loading) {
    return (
      <div className="page-container">
        <h2><svg className="icon"><use href="#i-home" /></svg> Dashboard</h2>
        <p>Loading...</p>
      </div>
    )
  }

  if (error) {
    return (
      <div className="page-container">
        <h2><svg className="icon"><use href="#i-home" /></svg> Dashboard</h2>
        <p className="error">{error}</p>
      </div>
    )
  }

  if (players.length === 0) {
    return (
      <div className="page-container">
        <h2><svg className="icon"><use href="#i-home" /></svg> Dashboard</h2>
        <div className="cta-section">
          <h3>Add your first player</h3>
          <p>Create a player profile to start tracking games.</p>
          <Link className="cta-btn" to="/players"><svg className="icon"><use href="#i-user" /></svg> Add a Player</Link>
          <p style={{ marginTop: '1rem' }}>
            Are you a player joining a parent's account? <Link to="/join">Enter your invite code</Link>
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="page-container">
      <h2>
        <svg className="icon"><use href="#i-home" /></svg>
        {isPlayerRole ? 'My Dashboard' : `Season Dashboard${currentSeason ? ` - ${currentSeason.name}` : ''}`}
      </h2>

      <div className="dashboard-grid">
        {players.map(({ player, teamStats, ibba, gamesByTeam }) => (
          <div className="card player-card-v2" key={player.id}>
            <div className="player-card-head">
              <div className="player-card-avatar">{player.firstName[0]}{player.lastName[0]}</div>
              <div>
                <h3>
                  {player.firstName} {player.lastName} <span className="player-card-number">#{player.jerseyNumber}</span>
                  {ibba && <IbbaBadge />}
                </h3>
                <p className="player-card-role">{player.position || 'Player'}</p>
              </div>
            </div>
            {teamStats.length > 0 ? (
              <div className="player-card-teams">
                {teamStats.map((s) => {
                  const ibbaTeam = ibba?.teams.find((t) => t.linkedTeamId === s.teamId)
                  const { last, next } = lastAndNextGame(gamesByTeam[s.teamId] ?? [])
                  return (
                    <div className="player-card-team-row" key={s.teamId}>
                      <div className="flex" style={{ justifyContent: 'space-between', gap: '0.6rem' }}>
                        <div className="flex" style={{ gap: '0.5rem', minWidth: 0 }}>
                          <TeamCrest
                            logoUrl={ibbaTeam?.teamLogoUrl}
                            jerseyNumber={s.jerseyNumber}
                            showIbbaMark={!!ibbaTeam}
                            size="sm"
                            onClick={ibbaTeam?.ibbaLeagueUrl ? () => setStandingsFor({ leagueUrl: ibbaTeam.ibbaLeagueUrl!, leagueName: ibbaTeam.ibbaLeagueName ?? '', teamName: s.teamName }) : undefined}
                            title={ibbaTeam?.ibbaLeagueUrl ? 'View standings' : undefined}
                          />
                          <div style={{ minWidth: 0 }}>
                            <span className="player-card-team-name">{s.teamName}</span>
                            {ibbaTeam?.ibbaLeagueName && (
                              <div style={{ fontSize: '0.7rem', color: 'var(--color-text-faint)' }} dir="rtl">{ibbaTeam.ibbaLeagueName}</div>
                            )}
                          </div>
                        </div>
                        {ibbaTeam?.position && (
                          <button className="pos-pill" onClick={() => setStandingsFor({ leagueUrl: ibbaTeam.ibbaLeagueUrl!, leagueName: ibbaTeam.ibbaLeagueName ?? '', teamName: s.teamName })}>
                            <svg className="icon"><use href="#i-target" /></svg>
                            {ibbaTeam.position}{ibbaTeam.position === 1 ? 'st' : ibbaTeam.position === 2 ? 'nd' : ibbaTeam.position === 3 ? 'rd' : 'th'} of {ibbaTeam.totalTeams}
                          </button>
                        )}
                      </div>
                      <div className="player-card-team-stats">
                        <div className="tabular"><b>{s.pointsPerGame}</b><span>PPG</span></div>
                        <div className="tabular"><b>{s.reboundsPerGame}</b><span>RPG</span></div>
                        <div className="tabular"><b>{s.assistsPerGame}</b><span>APG</span></div>
                      </div>
                      {(last || next) && (
                        <div className="glance-grid">
                          <div className="glance-block">
                            <span className="glance-label">Last</span>
                            {last ? (
                              <>
                                <div className="glance-line">{last.isHomeGame === false ? '✈️' : '🏠'} <span className="truncate">{last.opponentName}</span></div>
                                <span className={`glance-score ${(last.teamScore ?? 0) > (last.opponentScore ?? 0) ? 'win' : 'loss'}`}>
                                  {(last.teamScore ?? 0) > (last.opponentScore ?? 0) ? 'W' : 'L'} {last.teamScore}–{last.opponentScore}
                                </span>
                              </>
                            ) : <span className="glance-line" style={{ color: 'var(--color-text-faint)' }}>None yet</span>}
                          </div>
                          <div className="glance-block">
                            <span className="glance-label">Next</span>
                            {next ? (
                              <>
                                <div className="glance-line">{next.isHomeGame === false ? '✈️' : '🏠'} <span className="truncate">{next.opponentName}</span></div>
                                <span className="glance-score upcoming">{new Date(next.gameDate).toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' })}</span>
                              </>
                            ) : <span className="glance-line" style={{ color: 'var(--color-text-faint)' }}>None scheduled</span>}
                          </div>
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            ) : (
              <p>No team yet</p>
            )}
            <Link className="view-link" to="/stats">View Stats →</Link>
          </div>
        ))}
      </div>

      {standingsFor && (
        <StandingsModal
          leagueUrl={standingsFor.leagueUrl}
          leagueName={standingsFor.leagueName}
          highlightTeamName={standingsFor.teamName}
          onClose={() => setStandingsFor(null)}
        />
      )}
    </div>
  )
}
