import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import type { GameDto, GameType, ShotDto, UpdateGameDto } from '../api/types'
import CourtShotChart from '../components/CourtShotChart'
import GameStatusBadge from '../components/GameStatusBadge'

interface EditForm {
  opponentName: string
  gameDate: string
  location: string
  gameType: GameType
  teamScore: string
  opponentScore: string
  notes: string
}

function toEditForm(game: GameDto): EditForm {
  return {
    opponentName: game.opponentName,
    gameDate: game.gameDate.split('T')[0],
    location: game.location,
    gameType: game.gameType,
    teamScore: game.teamScore != null ? String(game.teamScore) : '',
    opponentScore: game.opponentScore != null ? String(game.opponentScore) : '',
    notes: game.notes ?? '',
  }
}

export default function GameDetail() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [game, setGame] = useState<GameDto | null>(null)
  const [shotsByStatsId, setShotsByStatsId] = useState<Record<number, ShotDto[]>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [sharing, setSharing] = useState(false)
  const [shareUrl, setShareUrl] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<EditForm | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)

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

  const startEditing = () => {
    if (!game) return
    setForm(toEditForm(game))
    setEditing(true)
  }

  const saveEdits = async () => {
    if (!game || !form) return
    setSaving(true)
    try {
      const dto: UpdateGameDto = {
        opponentName: form.opponentName.trim(),
        gameDate: new Date(form.gameDate).toISOString(),
        location: form.location.trim(),
        gameType: form.gameType,
        teamScore: form.teamScore === '' ? null : Number(form.teamScore),
        opponentScore: form.opponentScore === '' ? null : Number(form.opponentScore),
        notes: form.notes.trim(),
      }
      const { data } = await api.put<GameDto>(`/games/${game.id}`, dto)
      setGame(data)
      setEditing(false)
      setError(null)
    } catch {
      setError('Could not save those changes.')
    } finally {
      setSaving(false)
    }
  }

  const deleteGame = async () => {
    if (!game) return
    if (!window.confirm(`Delete this game vs ${game.opponentName}? This removes all of its stats and can't be undone.`)) return
    setDeleting(true)
    try {
      await api.delete(`/games/${game.id}`)
      navigate('/stats', { replace: true })
    } catch {
      setError('Could not delete this game.')
      setDeleting(false)
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
        <div className="flex gap-1">
          <button className="submit-btn" onClick={shareGame} disabled={sharing}>
            {sharing ? 'Creating link...' : '🔗 Share This Game'}
          </button>
          {!editing && (
            <button className="submit-btn" onClick={startEditing}>✏️ Edit</button>
          )}
          <button className="end-game-btn" onClick={deleteGame} disabled={deleting}>
            {deleting ? 'Deleting...' : '🗑️ Delete Game'}
          </button>
        </div>
      </div>

      {shareUrl && (
        <div className="invite-box">
          <p>Share link (copied to clipboard):</p>
          <code>{shareUrl}</code>
        </div>
      )}

      {error && <p className="error">{error}</p>}

      {editing && form ? (
        <div className="info-section" style={{ marginTop: '1.5rem' }}>
          <div className="form-row">
            <label>
              Opponent
              <input value={form.opponentName} onChange={(e) => setForm({ ...form, opponentName: e.target.value })} />
            </label>
            <label>
              Date
              <input type="date" value={form.gameDate} onChange={(e) => setForm({ ...form, gameDate: e.target.value })} />
            </label>
            <label>
              Location
              <input value={form.location} onChange={(e) => setForm({ ...form, location: e.target.value })} />
            </label>
            <label>
              Game Type
              <select value={form.gameType} onChange={(e) => setForm({ ...form, gameType: e.target.value as GameType })}>
                <option value="League">League</option>
                <option value="Cup">Cup</option>
              </select>
            </label>
            <label>
              {game.teamName} Score
              <input type="number" value={form.teamScore} onChange={(e) => setForm({ ...form, teamScore: e.target.value })} />
            </label>
            <label>
              Opponent Score
              <input type="number" value={form.opponentScore} onChange={(e) => setForm({ ...form, opponentScore: e.target.value })} />
            </label>
          </div>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', fontWeight: 600, marginBottom: '1rem' }}>
            Notes
            <textarea
              value={form.notes}
              onChange={(e) => setForm({ ...form, notes: e.target.value })}
              rows={3}
              style={{ resize: 'vertical', fontFamily: 'inherit' }}
            />
          </label>
          <div className="flex gap-1">
            <button className="submit-btn" onClick={saveEdits} disabled={saving}>
              {saving ? 'Saving...' : 'Save Changes'}
            </button>
            <button className="nav-btn" onClick={() => setEditing(false)} disabled={saving}>Cancel</button>
          </div>
        </div>
      ) : game.status === 'Completed' ? (
        <div className="game-score">
          <div className={`score-display ${won ? 'win' : 'loss'}`}>
            <span>{game.teamScore}</span>
            <span className="vs">-</span>
            <span>{game.opponentScore}</span>
          </div>
        </div>
      ) : (
        <div className="game-upcoming">
          <p><GameStatusBadge status={game.status} /></p>
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
