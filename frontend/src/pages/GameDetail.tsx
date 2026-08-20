import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import type { GameDto, ShotDto } from '../api/types'
import CourtShotChart from '../components/CourtShotChart'

export default function GameDetail() {
  const { id } = useParams<{ id: string }>()
  const [game, setGame] = useState<GameDto | null>(null)
  const [shotsByStatsId, setShotsByStatsId] = useState<Record<number, ShotDto[]>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [sharing, setSharing] = useState(false)
  const [shareUrl, setShareUrl] = useState<string | null>(null)

  useEffect(() => {
    if (id) load(Number(id))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  const load = async (gameId: number) => {
    try {
      setLoading(true)
      const { data } = await api.get<GameDto>(`/games/${gameId}`)
      setGame(data)

      const shotLists = await Promise.all(
        data.playerStats.map((stats) => api.get<ShotDto[]>(`/shots/gamestats/${stats.id}`).then((res) => [stats.id, res.data] as const))
      )
      setShotsByStatsId(Object.fromEntries(shotLists))
      setError(null)
    } catch {
      setError('Could not load this game.')
    } finally {
      setLoading(false)
    }
  }

  const shareGame = async () => {
    if (!game || game.playerStats.length === 0) return
    setSharing(true)
    try {
      const { data } = await api.post('/share', { playerId: game.playerStats[0].playerId, gameId: game.id })
      setShareUrl(`${window.location.origin}/share/${data.token}`)
      try {
        await navigator.clipboard.writeText(`${window.location.origin}/share/${data.token}`)
      } catch {
        // clipboard may be unavailable
      }
    } catch {
      setError('Could not create a share link.')
    } finally {
      setSharing(false)
    }
  }

  if (loading) {
    return (
      <div className="page-container">
        <h2>📅 Game Detail</h2>
        <p>Loading...</p>
      </div>
    )
  }

  if (error || !game) {
    return (
      <div className="page-container">
        <h2>📅 Game Detail</h2>
        <p className="error">{error ?? 'Game not found.'}</p>
        <Link to="/games">← Back to Games</Link>
      </div>
    )
  }

  const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)

  return (
    <div className="page-container">
      <Link to="/games" className="back-link">← Back to Games</Link>

      <div className="game-detail-header">
        <div>
          <h2>
            vs {game.opponentName}
            <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>{game.gameType}</span>
          </h2>
          <p>{game.teamName} · {new Date(game.gameDate).toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' })} · 📍 {game.location || 'TBD'}</p>
        </div>
        <button className="submit-btn" onClick={shareGame} disabled={sharing}>
          {sharing ? 'Creating link...' : '🔗 Share This Game'}
        </button>
      </div>

      {shareUrl && (
        <div className="invite-box">
          <p>Share link (copied to clipboard):</p>
          <code>{shareUrl}</code>
        </div>
      )}

      {game.status === 'Completed' ? (
        <div className="game-score">
          <div className={`score-display ${won ? 'win' : 'loss'}`}>
            <span>{game.teamScore}</span>
            <span className="vs">-</span>
            <span>{game.opponentScore}</span>
          </div>
        </div>
      ) : (
        <div className="game-upcoming">
          <p>{game.status}</p>
        </div>
      )}

      {game.notes && (
        <div className="info-section" style={{ marginTop: '1.5rem' }}>
          <p>{game.notes}</p>
        </div>
      )}

      {game.playerStats.map((stats) => (
        <div key={stats.id} className="stats-card-enhanced" style={{ marginTop: '1.75rem' }}>
          <div className="player-header-enhanced">
            <div className="player-info">
              <div>
                <h3>{stats.playerName}</h3>
                <p>{stats.minutesPlayed} minutes played</p>
              </div>
            </div>
            <div className="games-badge">{stats.totalPoints} PTS</div>
          </div>

          <div className="primary-stats">
            <h4>Box Score</h4>
            <div className="stats-grid-enhanced">
              <div className="stat-box-enhanced featured">
                <span className="stat-value">{stats.totalPoints}</span>
                <span className="stat-label">PTS</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.totalRebounds}</span>
                <span className="stat-label">REB</span>
                <span className="stat-detail">{stats.offensiveRebounds} off · {stats.defensiveRebounds} def</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.assists}</span>
                <span className="stat-label">AST</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.steals}</span>
                <span className="stat-label">STL</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.blocks}</span>
                <span className="stat-label">BLK</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.turnovers}</span>
                <span className="stat-label">TO</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.fouls}</span>
                <span className="stat-label">FOULS</span>
              </div>
            </div>
          </div>

          <div className="shooting-stats">
            <h4>Shooting</h4>
            <div className="percentage-bars">
              <div className="percentage-item">
                <div className="percentage-label">2PT {stats.fieldGoalsMade}/{stats.fieldGoalsAttempted}</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${stats.fieldGoalPercentage}%` }} /></div>
                <span className="percentage-value">{stats.fieldGoalPercentage}%</span>
              </div>
              <div className="percentage-item">
                <div className="percentage-label">3PT {stats.threePointersMade}/{stats.threePointersAttempted}</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${stats.threePointPercentage}%` }} /></div>
                <span className="percentage-value">{stats.threePointPercentage}%</span>
              </div>
              <div className="percentage-item">
                <div className="percentage-label">FT {stats.freeThrowsMade}/{stats.freeThrowsAttempted}</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${stats.freeThrowPercentage}%` }} /></div>
                <span className="percentage-value">{stats.freeThrowPercentage}%</span>
              </div>
            </div>
          </div>

          <div className="season-chart-section">
            <h4>🎯 Shot Chart</h4>
            {(shotsByStatsId[stats.id]?.length ?? 0) === 0 ? (
              <p className="no-shots-note">No shots logged for this game.</p>
            ) : (
              <CourtShotChart shots={shotsByStatsId[stats.id]} interactive={false} />
            )}
          </div>
        </div>
      ))}
    </div>
  )
}
