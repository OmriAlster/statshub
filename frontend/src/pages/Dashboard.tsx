import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { GameDto, PlayerDto, PlayerTeamStatsDto, SeasonDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import GameStatusBadge from '../components/GameStatusBadge'

interface PlayerCard {
  player: PlayerDto
  teamStats: PlayerTeamStatsDto[]
}

export default function Dashboard() {
  const { user } = useAuth()
  const isPlayerRole = user?.role === 'Player'
  const [players, setPlayers] = useState<PlayerCard[]>([])
  const [currentSeason, setCurrentSeason] = useState<SeasonDto | null>(null)
  const [recentGames, setRecentGames] = useState<GameDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

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
        basePlayers.map(async (player) => {
          try {
            const { data: teamStats } = await api.get<PlayerTeamStatsDto[]>(`/gamestats/player/${player.id}`)
            return { player, teamStats }
          } catch {
            return { player, teamStats: [] }
          }
        })
      )

      const recentGamesPromise =
        basePlayers.length > 0
          ? api.get<GameDto[]>(`/games/player/${basePlayers[0].id}`).then((res) => res.data.slice(0, 5))
          : Promise.resolve<GameDto[]>([])

      const [cards, recentGames] = await Promise.all([cardsPromise, recentGamesPromise])
      setPlayers(cards)
      setRecentGames(recentGames)

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
        {players.map(({ player, teamStats }) => (
          <div className="card player-card-v2" key={player.id}>
            <div className="player-card-head">
              <div className="player-card-avatar">{player.firstName[0]}{player.lastName[0]}</div>
              <div>
                <h3>{player.firstName} {player.lastName} <span className="player-card-number">#{player.jerseyNumber}</span></h3>
                <p className="player-card-role">{player.position || 'Player'}</p>
              </div>
            </div>
            {teamStats.length > 0 ? (
              <div className="player-card-teams">
                {teamStats.map((s) => (
                  <div className="player-card-team-row" key={s.teamId}>
                    <span className="player-card-team-name">{s.teamName} <span className="player-card-number">#{s.jerseyNumber}</span></span>
                    <div className="player-card-team-stats">
                      <div className="tabular"><b>{s.pointsPerGame}</b><span>PPG</span></div>
                      <div className="tabular"><b>{s.reboundsPerGame}</b><span>RPG</span></div>
                      <div className="tabular"><b>{s.assistsPerGame}</b><span>APG</span></div>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <p>No team yet</p>
            )}
            <Link className="view-link" to="/stats">View Stats →</Link>
          </div>
        ))}
      </div>

      <div className="recent-games-section">
        <h3><svg className="icon"><use href="#i-live" /></svg> Recent Games</h3>
        {recentGames.length === 0 ? (
          <p>No games yet.</p>
        ) : (
          <div className="mini-game-list">
            {recentGames.map((game) => {
              const stats = game.playerStats[0]
              const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)
              return (
                <Link to={`/games/${game.id}`} className="mini-game" key={game.id}>
                  <div className="game-date">
                    {new Date(game.gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}
                  </div>
                  <div className="game-info">
                    <div>
                      vs {game.opponentName}
                      <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>{game.gameType}</span>
                    </div>
                    {stats && (
                      <div className="game-stats">{stats.totalPoints} pts • {stats.totalRebounds} reb • {stats.assists} ast</div>
                    )}
                  </div>
                  <div className={`game-result ${game.status === 'Completed' ? (won ? 'win' : 'loss') : ''}`}>
                    {game.status === 'Completed'
                      ? `${won ? 'W' : 'L'} ${game.teamScore}-${game.opponentScore}`
                      : <GameStatusBadge status={game.status} />}
                  </div>
                </Link>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
