import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api/client'
import type { SharedPlayerDto } from '../api/types'

export default function SharedPlayerView() {
  const { token } = useParams<{ token: string }>()
  const [data, setData] = useState<SharedPlayerDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!token) return
    try {
      const { data: shared } = await api.get<SharedPlayerDto>(`/share/${token}`)
      setData(shared)
      setError(null)
    } catch {
      setError('This share link is invalid or has expired.')
    } finally {
      setLoading(false)
    }
  }, [token])

  useEffect(() => {
    load()
  }, [load])

  // Poll while the shared game is still live so viewers see updates in near real time.
  // Keyed on status (not `data` itself) so the interval isn't torn down and
  // recreated on every single poll tick - only when the game actually ends.
  const gameStatus = data?.game?.status
  useEffect(() => {
    if (gameStatus !== 'In Progress') return
    const interval = setInterval(load, 5000)
    return () => clearInterval(interval)
  }, [gameStatus, load])

  if (loading) {
    return (
      <div className="login-container">
        <div className="login-box"><p>Loading...</p></div>
      </div>
    )
  }

  if (error || !data) {
    return (
      <div className="login-container">
        <div className="login-box">
          <h2>🏀 StatsHub</h2>
          <p className="error">{error ?? 'Not found'}</p>
        </div>
      </div>
    )
  }

  return (
    <div className="shared-view">
      <div className="page-container">
        <h2>🏀 {data.playerName} #{data.jerseyNumber}</h2>
        <p>{data.position}</p>

        {data.game ? (
          <SharedGame game={data.game} />
        ) : (
          <>
            <div className="stats-tables">
              {data.teams.map((team, i) => (
                <div key={i} className="stats-card-enhanced">
                  <div className="player-header-enhanced">
                    <div className="player-info">
                      <div className="jersey">{team.jerseyNumber}</div>
                      <div>
                        <h3>{team.playerName}</h3>
                        <p>{team.position} · {team.teamName}</p>
                      </div>
                    </div>
                    <div className="games-badge">{team.gamesPlayed} Games</div>
                  </div>
                  <div className="stats-grid-enhanced">
                    <div className="stat-box-enhanced">
                      <span className="stat-value">{team.pointsPerGame.toFixed(1)}</span>
                      <span className="stat-label">PPG</span>
                    </div>
                    <div className="stat-box-enhanced">
                      <span className="stat-value">{team.reboundsPerGame.toFixed(1)}</span>
                      <span className="stat-label">RPG</span>
                    </div>
                    <div className="stat-box-enhanced">
                      <span className="stat-value">{team.assistsPerGame.toFixed(1)}</span>
                      <span className="stat-label">APG</span>
                    </div>
                  </div>
                </div>
              ))}
            </div>

            <div className="recent-games-section">
              <h3>Recent Games</h3>
              <div className="mini-game-list">
                {data.recentGames.map((game) => {
                  const stats = game.playerStats[0]
                  const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)
                  return (
                    <div className="mini-game" key={game.id}>
                      <div className="game-date">
                        {new Date(game.gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}
                      </div>
                      <div className="game-info">
                        <div>
                          vs {game.opponentName}
                          <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>{game.gameType}</span>
                        </div>
                        {stats && <div className="game-stats">{stats.totalPoints} pts • {stats.totalRebounds} reb • {stats.assists} ast</div>}
                      </div>
                      <div className={`game-result ${game.status === 'Completed' ? (won ? 'win' : 'loss') : ''}`}>
                        {game.status === 'Completed' ? `${won ? 'W' : 'L'} ${game.teamScore}-${game.opponentScore}` : game.status}
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          </>
        )}

        <p className="shared-footer">Shared via StatsHub 🏀</p>
      </div>
    </div>
  )
}

function SharedGame({ game }: { game: SharedPlayerDto['game'] }) {
  if (!game) return null
  const stats = game.playerStats[0]
  return (
    <div className="stats-card-enhanced">
      {game.status === 'In Progress' && <div className="live-badge">🔴 LIVE</div>}
      <h3>
        vs {game.opponentName}
        <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>{game.gameType}</span>
      </h3>
      <p>{game.teamName} • {new Date(game.gameDate).toLocaleDateString()} • {game.location}</p>
      {game.status === 'Completed' && (
        <div className="score-display">
          <span>{game.teamScore}</span>
          <span className="vs">-</span>
          <span>{game.opponentScore}</span>
        </div>
      )}
      {stats && (
        <div className="stats-grid-enhanced">
          <div className="stat-box-enhanced"><span className="stat-value">{stats.totalPoints}</span><span className="stat-label">Points</span></div>
          <div className="stat-box-enhanced"><span className="stat-value">{stats.totalRebounds}</span><span className="stat-label">Rebounds</span></div>
          <div className="stat-box-enhanced"><span className="stat-value">{stats.assists}</span><span className="stat-label">Assists</span></div>
          <div className="stat-box-enhanced"><span className="stat-value">{stats.steals}</span><span className="stat-label">Steals</span></div>
          <div className="stat-box-enhanced"><span className="stat-value">{stats.blocks}</span><span className="stat-label">Blocks</span></div>
        </div>
      )}
      <div className="breakdown">
        2P: {stats?.fieldGoalsMade}/{stats?.fieldGoalsAttempted} | 3P: {stats?.threePointersMade}/{stats?.threePointersAttempted} | FT: {stats?.freeThrowsMade}/{stats?.freeThrowsAttempted}
      </div>
    </div>
  )
}
