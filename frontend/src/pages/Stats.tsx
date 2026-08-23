import { Fragment, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import type {
  CreateGameStatsDto,
  GameDto,
  GameType,
  IbbaLinkStatusDto,
  PlayerDto,
  PlayerTeamStatsDto,
  ShotDto,
  UpdateGameDto,
} from '../api/types'
import { useAuth } from '../auth/AuthContext'
import CourtShotChart from '../components/CourtShotChart'
import GameStatsEditor from '../components/GameStatsEditor'
import GameStatusBadge from '../components/GameStatusBadge'
import SegmentedControl from '../components/SegmentedControl'
import StandingsModal from '../components/StandingsModal'
import TeamCrest from '../components/TeamCrest'
import { useLiveGameOverlay } from '../live/LiveGameContext'

export default function Stats() {
  const { user } = useAuth()
  const isPlayerRole = user?.role === 'Player'
  const { playerId: playerIdParam } = useParams<{ playerId: string }>()
  const navigate = useNavigate()

  const [players, setPlayers] = useState<PlayerDto[]>([])
  const [playersLoading, setPlayersLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    loadPlayers()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const loadPlayers = async () => {
    try {
      const list = isPlayerRole && user?.linkedPlayer ? [user.linkedPlayer] : (await api.get<PlayerDto[]>('/players')).data
      setPlayers(list)
      // Every player gets their own URL - land on the first one if none was
      // requested, so switching players is real navigation, not just local state.
      if (!playerIdParam && list.length > 0) navigate(`/stats/${list[0].id}`, { replace: true })
    } catch {
      setError('Could not load players.')
    } finally {
      setPlayersLoading(false)
    }
  }

  const selectedPlayer = players.find((p) => p.id === Number(playerIdParam)) ?? players[0]

  return (
    <div className="page-container">
      <h2>📈 Profiles</h2>

      {error && <p className="error">{error}</p>}

      {playersLoading ? (
        <p>Loading...</p>
      ) : players.length === 0 ? (
        <p>No players yet.</p>
      ) : (
        <>
          {players.length > 1 && (
            <div className="profile-picker">
              {players.map((p) => (
                <Link
                  key={p.id}
                  to={`/stats/${p.id}`}
                  className={`profile-picker-item ${selectedPlayer?.id === p.id ? 'active' : ''}`}
                >
                  {p.profilePictureUrl ? (
                    <img className="profile-picker-avatar" src={p.profilePictureUrl} alt="" />
                  ) : (
                    <span className="profile-picker-avatar">{p.firstName[0]}{p.lastName[0]}</span>
                  )}
                  <span className="profile-picker-name">{p.firstName} {p.lastName}</span>
                </Link>
              ))}
            </div>
          )}

          {selectedPlayer && <PlayerProfilePanel key={selectedPlayer.id} player={selectedPlayer} />}
        </>
      )}
    </div>
  )
}

interface TeamMeta {
  id: number
  name: string
  jerseyNumber?: number | null
  logoUrl?: string | null
  isIbba: boolean
  leagueUrl?: string | null
  leagueName?: string | null
  position?: number | null
  totalTeams?: number | null
}

function PlayerProfilePanel({ player }: { player: PlayerDto }) {
  const [tab, setTab] = useState<'stats' | 'schedule' | 'season'>('stats')
  const [games, setGames] = useState<GameDto[]>([])
  const [ibba, setIbba] = useState<IbbaLinkStatusDto | null>(null)
  const [seasonStats, setSeasonStats] = useState<PlayerTeamStatsDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selectedTeamId, setSelectedTeamId] = useState<number | 'all' | null>(null)
  const [standingsFor, setStandingsFor] = useState<{ leagueUrl: string; leagueName: string; teamName: string } | null>(null)

  useEffect(() => {
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [player.id])

  const load = async () => {
    try {
      setLoading(true)
      const [{ data: gamesData }, ibbaData, statsData] = await Promise.all([
        api.get<GameDto[]>(`/games/player/${player.id}`),
        api.get<IbbaLinkStatusDto>(`/players/${player.id}/ibba`).then((res) => res.data).catch(() => null),
        api.get<PlayerTeamStatsDto[]>(`/gamestats/player/${player.id}`).then((res) => res.data).catch(() => []),
      ])
      setGames(gamesData)
      setIbba(ibbaData)
      setSeasonStats(statsData)
      setError(null)
    } catch {
      setError('Could not load this player.')
    } finally {
      setLoading(false)
    }
  }

  const teamMeta = useMemo(() => {
    const meta: Record<number, TeamMeta> = {}
    for (const t of player.teams ?? []) {
      meta[t.id] = { id: t.id, name: t.name, jerseyNumber: t.jerseyNumber, isIbba: false }
    }
    for (const g of games) {
      if (!meta[g.teamId]) meta[g.teamId] = { id: g.teamId, name: g.teamName, isIbba: false }
    }
    for (const it of ibba?.teams ?? []) {
      if (it.linkedTeamId != null) {
        meta[it.linkedTeamId] = {
          ...meta[it.linkedTeamId],
          id: it.linkedTeamId,
          name: meta[it.linkedTeamId]?.name ?? it.teamName,
          logoUrl: it.teamLogoUrl,
          isIbba: true,
          leagueUrl: it.ibbaLeagueUrl,
          leagueName: it.ibbaLeagueName,
          position: it.position,
          totalTeams: it.totalTeams,
        }
      }
    }
    return meta
  }, [player.teams, games, ibba])

  const teamList = useMemo(() => Object.values(teamMeta), [teamMeta])

  useEffect(() => {
    if (selectedTeamId !== null && (selectedTeamId === 'all' || teamMeta[selectedTeamId])) return
    if (teamList.length > 0) setSelectedTeamId(teamList[0].id)
    else setSelectedTeamId('all')
  }, [teamList, teamMeta, selectedTeamId])

  const updateGame = (updated: GameDto) => setGames((prev) => prev.map((g) => (g.id === updated.id ? updated : g)))
  const removeGame = (id: number) => setGames((prev) => prev.filter((g) => g.id !== id))
  const addGame = (created: GameDto) => setGames((prev) => [...prev, created])

  if (loading) return <p>Loading...</p>

  const selectedTeam = selectedTeamId !== null && selectedTeamId !== 'all' ? teamMeta[selectedTeamId] : null
  const gamesForTeam = selectedTeamId === 'all' || selectedTeamId === null ? games : games.filter((g) => g.teamId === selectedTeamId)

  return (
    <div>
      {error && <p className="error">{error}</p>}

      {teamList.length > 1 && (
        <div className="stats-team-switch">
          <SegmentedControl
            options={[
              { value: 'all' as const, label: 'All Teams' },
              ...teamList.map((t) => ({
                value: t.id,
                label: (
                  <span className="team-switch-option">
                    {t.logoUrl ? <img src={t.logoUrl} alt="" /> : <span className="team-switch-option-fallback" />}
                    {t.name}
                  </span>
                ),
              })),
            ]}
            value={selectedTeamId ?? 'all'}
            onChange={setSelectedTeamId}
          />
        </div>
      )}

      {selectedTeam && (
        <div className="team-meta-strip">
          <TeamCrest
            logoUrl={selectedTeam.logoUrl}
            jerseyNumber={selectedTeam.jerseyNumber}
            showIbbaMark={selectedTeam.isIbba}
            onClick={selectedTeam.leagueUrl ? () => setStandingsFor({ leagueUrl: selectedTeam.leagueUrl!, leagueName: selectedTeam.leagueName ?? '', teamName: selectedTeam.name }) : undefined}
            title={selectedTeam.leagueUrl ? 'View standings' : undefined}
          />
          <div>
            <div className="pctr-team-name">{selectedTeam.name}</div>
            {selectedTeam.leagueName && (
              <button
                className="league-chip"
                style={{ marginTop: '0.3rem' }}
                onClick={() => setStandingsFor({ leagueUrl: selectedTeam.leagueUrl!, leagueName: selectedTeam.leagueName ?? '', teamName: selectedTeam.name })}
              >
                <svg className="icon"><use href="#i-trophy" /></svg>
                <span dir="rtl">{selectedTeam.leagueName}</span>
                {selectedTeam.position && ` · ${selectedTeam.position} of ${selectedTeam.totalTeams}`}
              </button>
            )}
          </div>
        </div>
      )}

      <SegmentedControl
        className="stats-tab-switch"
        options={[
          { value: 'stats', label: '📊 Stats' },
          { value: 'schedule', label: '🗓️ Schedule' },
          { value: 'season', label: '📈 Season' },
        ]}
        value={tab}
        onChange={setTab}
      />

      {tab === 'stats' && <StatsPanel games={gamesForTeam} />}
      {tab === 'schedule' && (
        <SchedulePanel
          player={player}
          games={gamesForTeam}
          teamList={teamList}
          selectedTeamId={selectedTeamId}
          onGameUpdated={updateGame}
          onGameDeleted={removeGame}
          onGameCreated={addGame}
        />
      )}
      {tab === 'season' && <SeasonPanel player={player} stats={seasonStats} selectedTeamId={selectedTeamId} />}

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

function StatsPanel({ games }: { games: GameDto[] }) {
  const { completedGames, wins, losses, ppg, averages } = useMemo(() => {
    const completedGames = games
      .filter((g) => g.status === 'Completed')
      .sort((a, b) => new Date(b.gameDate).getTime() - new Date(a.gameDate).getTime())
    // Team record reflects every completed game regardless of whether this
    // player's box score has been tracked yet (e.g. a game just synced from
    // IBBA). Personal averages below must not count those - an untracked
    // game has no points to report, not zero points.
    const wins = completedGames.filter((g) => (g.teamScore ?? 0) > (g.opponentScore ?? 0)).length
    const losses = completedGames.filter((g) => (g.teamScore ?? 0) < (g.opponentScore ?? 0)).length
    const gamesWithStats = completedGames.filter((g) => g.playerStats.length > 0)

    const avg = (pick: (g: GameDto) => number) =>
      gamesWithStats.length ? gamesWithStats.reduce((sum, g) => sum + pick(g), 0) / gamesWithStats.length : 0
    const ppg = avg((g) => g.playerStats[0]?.totalPoints ?? 0).toFixed(1)
    const averages = gamesWithStats.length
      ? {
          count: gamesWithStats.length,
          pts: ppg,
          fgm: avg((g) => g.playerStats[0]?.fieldGoalsMade ?? 0).toFixed(1),
          fga: avg((g) => g.playerStats[0]?.fieldGoalsAttempted ?? 0).toFixed(1),
          tpm: avg((g) => g.playerStats[0]?.threePointersMade ?? 0).toFixed(1),
          tpa: avg((g) => g.playerStats[0]?.threePointersAttempted ?? 0).toFixed(1),
          ftm: avg((g) => g.playerStats[0]?.freeThrowsMade ?? 0).toFixed(1),
          fta: avg((g) => g.playerStats[0]?.freeThrowsAttempted ?? 0).toFixed(1),
          reb: avg((g) => g.playerStats[0]?.totalRebounds ?? 0).toFixed(1),
          ast: avg((g) => g.playerStats[0]?.assists ?? 0).toFixed(1),
          stl: avg((g) => g.playerStats[0]?.steals ?? 0).toFixed(1),
          blk: avg((g) => g.playerStats[0]?.blocks ?? 0).toFixed(1),
          to: avg((g) => g.playerStats[0]?.turnovers ?? 0).toFixed(1),
        }
      : null

    return { completedGames, wins, losses, ppg, averages }
  }, [games])

  return (
    <div>
      <div className="season-summary">
        <div className="summary-stat">
          <span className="summary-value">{averages?.count ?? 0}</span>
          <span className="summary-label">Games Played</span>
        </div>
        <div className="summary-stat">
          <span className="summary-value">{ppg}</span>
          <span className="summary-label">PPG</span>
        </div>
        <div className="summary-stat">
          <span className="summary-value">{wins}-{losses}</span>
          <span className="summary-label">Record</span>
        </div>
      </div>

      {completedGames.length === 0 ? (
        <p>No completed games yet.</p>
      ) : (
        <div className="games-table-wrap">
          <table className="games-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Opponent</th>
                <th>Type</th>
                <th className="num">Score</th>
                <th className="num">Pts</th>
                <th className="num">2PT</th>
                <th className="num">3PT</th>
                <th className="num">FT</th>
                <th className="num">Reb</th>
                <th className="num">Ast</th>
                <th className="num">Stl</th>
                <th className="num">Blk</th>
                <th className="num">TO</th>
              </tr>
            </thead>
            <tbody>
              {completedGames.map((game) => {
                const stats = game.playerStats[0]
                const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)
                return (
                  <tr key={game.id}>
                    <td>
                      <Link to={`/games/${game.id}`} className="games-table-date-link">
                        {new Date(game.gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}
                      </Link>
                    </td>
                    <td>
                      {game.isHomeGame != null && <span title={game.isHomeGame ? 'Home' : 'Away'}>{game.isHomeGame ? '🏠' : '✈️'} </span>}
                      <Link to={`/games/${game.id}`}>{game.opponentName}</Link>
                      {game.isFromIbba && (
                        <img
                          src="/icons/ibba-logo.png"
                          alt=""
                          title="Synced from IBBA"
                          style={{ width: 12, height: 12, marginLeft: '0.35rem', verticalAlign: '-1px', borderRadius: 2 }}
                        />
                      )}
                    </td>
                    <td>
                      <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>{game.gameType}</span>
                    </td>
                    <td className={`num ${won ? 'win' : 'loss'}`}>
                      {game.teamScore}&ndash;{game.opponentScore}
                    </td>
                    <td className="num">{stats?.totalPoints ?? '-'}</td>
                    <td className="num">{stats ? `${stats.fieldGoalsMade}/${stats.fieldGoalsAttempted}` : '-'}</td>
                    <td className="num">{stats ? `${stats.threePointersMade}/${stats.threePointersAttempted}` : '-'}</td>
                    <td className="num">{stats ? `${stats.freeThrowsMade}/${stats.freeThrowsAttempted}` : '-'}</td>
                    <td className="num">{stats?.totalRebounds ?? '-'}</td>
                    <td className="num">{stats?.assists ?? '-'}</td>
                    <td className="num">{stats?.steals ?? '-'}</td>
                    <td className="num">{stats?.blocks ?? '-'}</td>
                    <td className="num">{stats?.turnovers ?? '-'}</td>
                  </tr>
                )
              })}
              {averages && (
                <tr className="avg-row">
                  <td colSpan={3}>Avg</td>
                  <td className="num">&mdash;</td>
                  <td className="num">{averages.pts}</td>
                  <td className="num">{averages.fgm}/{averages.fga}</td>
                  <td className="num">{averages.tpm}/{averages.tpa}</td>
                  <td className="num">{averages.ftm}/{averages.fta}</td>
                  <td className="num">{averages.reb}</td>
                  <td className="num">{averages.ast}</td>
                  <td className="num">{averages.stl}</td>
                  <td className="num">{averages.blk}</td>
                  <td className="num">{averages.to}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function SchedulePanel({
  player,
  games,
  teamList,
  selectedTeamId,
  onGameUpdated,
  onGameDeleted,
  onGameCreated,
}: {
  player: PlayerDto
  games: GameDto[]
  teamList: TeamMeta[]
  selectedTeamId: number | 'all' | null
  onGameUpdated: (g: GameDto) => void
  onGameDeleted: (id: number) => void
  onGameCreated: (g: GameDto) => void
}) {
  const { goLive } = useLiveGameOverlay()
  const [editingId, setEditingId] = useState<number | null>(null)
  const [deletingId, setDeletingId] = useState<number | null>(null)
  const [showAddForm, setShowAddForm] = useState(false)

  const sorted = useMemo(
    () => [...games].sort((a, b) => new Date(a.gameDate).getTime() - new Date(b.gameDate).getTime()),
    [games]
  )

  const deleteGame = async (game: GameDto) => {
    if (!window.confirm(`Delete this game vs ${game.opponentName}? This removes all of its stats and can't be undone.`)) return
    setDeletingId(game.id)
    try {
      await api.delete(`/games/${game.id}`)
      onGameDeleted(game.id)
    } catch {
      window.alert('Could not delete this game.')
    } finally {
      setDeletingId(null)
    }
  }

  return (
    <div>
      <div className="flex gap-1" style={{ marginBottom: '0.85rem' }}>
        <button className="add-team-btn" onClick={() => setShowAddForm((v) => !v)}>
          {showAddForm ? 'Cancel' : '+ Schedule Game'}
        </button>
      </div>

      {showAddForm && (
        <ScheduleNewGamePanel
          teamList={teamList}
          selectedTeamId={selectedTeamId}
          onCreated={(created) => { onGameCreated(created); setShowAddForm(false) }}
          onCancel={() => setShowAddForm(false)}
        />
      )}

      {sorted.length === 0 ? (
        <p>No games scheduled yet.</p>
      ) : (
      <div className="games-table-wrap">
      <table className="games-table">
        <thead>
          <tr>
            <th>Date</th>
            <th>Opponent</th>
            <th>Type</th>
            <th className="num">Result</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((game) => {
            const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)
            return (
              <Fragment key={game.id}>
                <tr className={game.status !== 'Completed' ? 'upcoming-row' : ''}>
                  <td>{new Date(game.gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}</td>
                  <td>
                    {game.isHomeGame != null && <span title={game.isHomeGame ? 'Home' : 'Away'}>{game.isHomeGame ? '🏠' : '✈️'} </span>}
                    {game.opponentName}
                    {game.isFromIbba && (
                      <img
                        src="/icons/ibba-logo.png"
                        alt=""
                        title="Synced from IBBA"
                        style={{ width: 12, height: 12, marginLeft: '0.35rem', verticalAlign: '-1px', borderRadius: 2 }}
                      />
                    )}
                  </td>
                  <td>
                    <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>{game.gameType}</span>
                  </td>
                  {game.status === 'Completed' ? (
                    <td className={`num ${won ? 'win' : 'loss'}`}>
                      {won ? 'W' : 'L'} {game.teamScore}&ndash;{game.opponentScore}
                    </td>
                  ) : (
                    <td className="games-table-status"><GameStatusBadge status={game.status} /></td>
                  )}
                  <td>
                    <div className="flex gap-1">
                      {game.status === 'Upcoming' && (
                        <button
                          className="edit-btn"
                          title="Go live on this game"
                          onClick={() => goLive({ gameId: game.id, playerId: player.id })}
                        >
                          <svg className="icon"><use href="#i-live" /></svg>
                        </button>
                      )}
                      <button className="edit-btn" title="Edit game & stats" onClick={() => setEditingId(editingId === game.id ? null : game.id)}>
                        <svg className="icon"><use href="#i-edit" /></svg>
                      </button>
                      {!game.isFromIbba && (
                        <button
                          className="edit-btn"
                          title="Delete game"
                          disabled={deletingId === game.id}
                          onClick={() => deleteGame(game)}
                        >
                          🗑️
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
                {editingId === game.id && (
                  <tr className="edit-row">
                    <td colSpan={5}>
                      <ScheduleEditPanel
                        player={player}
                        game={game}
                        onGameUpdated={onGameUpdated}
                        onClose={() => setEditingId(null)}
                      />
                    </td>
                  </tr>
                )}
              </Fragment>
            )
          })}
        </tbody>
      </table>
      </div>
      )}
    </div>
  )
}

interface ScheduleNewGameForm {
  teamId: number | ''
  opponentName: string
  gameDate: string
  location: string
  gameType: GameType
}

function ScheduleNewGamePanel({
  teamList,
  selectedTeamId,
  onCreated,
  onCancel,
}: {
  teamList: TeamMeta[]
  selectedTeamId: number | 'all' | null
  onCreated: (g: GameDto) => void
  onCancel: () => void
}) {
  const defaultTeamId = selectedTeamId !== null && selectedTeamId !== 'all' ? selectedTeamId : teamList[0]?.id ?? ''
  const [form, setForm] = useState<ScheduleNewGameForm>({
    teamId: defaultTeamId,
    opponentName: '',
    gameDate: new Date().toISOString().split('T')[0],
    location: '',
    gameType: 'League',
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const create = async () => {
    if (!form.teamId || !form.opponentName.trim()) {
      setError('Pick a team and enter the opponent name.')
      return
    }
    setSaving(true)
    setError(null)
    try {
      const { data } = await api.post<GameDto>('/games', {
        teamId: form.teamId,
        gameType: form.gameType,
        opponentName: form.opponentName.trim(),
        gameDate: new Date(form.gameDate).toISOString(),
        location: form.location.trim(),
      })
      onCreated(data)
    } catch {
      setError('Could not schedule that game.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="edit-panel" style={{ marginBottom: '1rem' }}>
      <div className="form-row">
        {teamList.length > 1 && (
          <label>
            Team
            <select value={form.teamId} onChange={(e) => setForm({ ...form, teamId: Number(e.target.value) })}>
              {teamList.map((t) => (
                <option key={t.id} value={t.id}>{t.name}</option>
              ))}
            </select>
          </label>
        )}
        <label>
          Opponent
          <input value={form.opponentName} onChange={(e) => setForm({ ...form, opponentName: e.target.value })} placeholder="Opponent team name" />
        </label>
        <label>
          Date
          <input type="date" value={form.gameDate} onChange={(e) => setForm({ ...form, gameDate: e.target.value })} />
        </label>
        <label>
          Location
          <input value={form.location} onChange={(e) => setForm({ ...form, location: e.target.value })} placeholder="e.g. Home gym" />
        </label>
        <label>
          Type
          <select value={form.gameType} onChange={(e) => setForm({ ...form, gameType: e.target.value as GameType })}>
            <option value="League">League</option>
            <option value="Cup">Cup</option>
            <option value="Friendly">Friendly</option>
          </select>
        </label>
      </div>
      {error && <p className="error">{error}</p>}
      <div className="flex gap-1">
        <button className="submit-btn" onClick={create} disabled={saving}>{saving ? 'Scheduling...' : 'Schedule Game'}</button>
        <button className="nav-btn" onClick={onCancel} disabled={saving}>Cancel</button>
      </div>
    </div>
  )
}

interface EditGameForm {
  opponentName: string
  gameDate: string
  location: string
  gameType: GameType
  isHomeGame: 'home' | 'away'
  teamScore: string
  opponentScore: string
}

function ScheduleEditPanel({
  player,
  game,
  onGameUpdated,
  onClose,
}: {
  player: PlayerDto
  game: GameDto
  onGameUpdated: (game: GameDto) => void
  onClose: () => void
}) {
  const existingStats = game.playerStats.find((s) => s.playerId === player.id)

  const [form, setForm] = useState<EditGameForm>({
    opponentName: game.opponentName,
    gameDate: game.gameDate.split('T')[0],
    location: game.location,
    gameType: game.gameType,
    isHomeGame: game.isHomeGame === false ? 'away' : 'home',
    teamScore: game.teamScore != null ? String(game.teamScore) : '',
    opponentScore: game.opponentScore != null ? String(game.opponentScore) : '',
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // The box score below is edited live (tap the court, tap a stat button,
  // same as a live game) and saves itself as you go - only the game's own
  // fields (opponent, date, score, ...) need an explicit Save.
  const [statsSeed, setStatsSeed] = useState(existingStats ?? null)
  const [startingStats, setStartingStats] = useState(false)

  const saveGameInfo = async () => {
    setSaving(true)
    setError(null)
    try {
      const teamScore = form.teamScore === '' ? null : Number(form.teamScore)
      const opponentScore = form.opponentScore === '' ? null : Number(form.opponentScore)
      const dto: UpdateGameDto = {
        opponentName: form.opponentName.trim(),
        gameDate: new Date(form.gameDate).toISOString(),
        location: form.location.trim(),
        gameType: form.gameType,
        teamScore,
        opponentScore,
        status: teamScore != null && opponentScore != null ? 'Completed' : 'Upcoming',
        isHomeGame: form.isHomeGame === 'home',
      }
      const { data: updatedGame } = await api.put<GameDto>(`/games/${game.id}`, dto)
      onGameUpdated(updatedGame)
    } catch {
      setError('Could not save those changes.')
    } finally {
      setSaving(false)
    }
  }

  const startTrackingStats = async () => {
    setStartingStats(true)
    setError(null)
    try {
      const emptyStats: CreateGameStatsDto = {
        gameId: game.id,
        playerId: player.id,
        fieldGoalsMade: 0,
        fieldGoalsAttempted: 0,
        threePointersMade: 0,
        threePointersAttempted: 0,
        freeThrowsMade: 0,
        freeThrowsAttempted: 0,
        offensiveRebounds: 0,
        defensiveRebounds: 0,
        assists: 0,
        steals: 0,
        blocks: 0,
        turnovers: 0,
        fouls: 0,
        minutesPlayed: 0,
      }
      const { data } = await api.post('/gamestats', emptyStats)
      setStatsSeed(data)
      const { data: updatedGame } = await api.get<GameDto>(`/games/${game.id}`)
      onGameUpdated(updatedGame)
    } catch {
      setError('Could not start tracking stats for this game.')
    } finally {
      setStartingStats(false)
    }
  }

  return (
    <div className="edit-panel">
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
          Type
          <select value={form.gameType} onChange={(e) => setForm({ ...form, gameType: e.target.value as GameType })}>
            <option value="League">League</option>
            <option value="Cup">Cup</option>
            <option value="Friendly">Friendly</option>
          </select>
        </label>
        <label>
          Home/Away
          <select value={form.isHomeGame} onChange={(e) => setForm({ ...form, isHomeGame: e.target.value as 'home' | 'away' })}>
            <option value="home">🏠 Home</option>
            <option value="away">✈️ Away</option>
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

      {error && <p className="error">{error}</p>}
      <div className="flex gap-1">
        <button className="submit-btn" onClick={saveGameInfo} disabled={saving}>{saving ? 'Saving...' : 'Save Game Info'}</button>
      </div>

      <h4 style={{ marginTop: '1.25rem' }}>Box Score</h4>
      {statsSeed ? (
        <GameStatsEditor gameStatsId={statsSeed.id} initialStats={statsSeed} />
      ) : (
        <button className="submit-btn" onClick={startTrackingStats} disabled={startingStats}>
          {startingStats ? 'Starting...' : 'Start Tracking Stats'}
        </button>
      )}

      <div className="flex gap-1" style={{ marginTop: '1rem' }}>
        <button className="nav-btn" onClick={onClose}>Done</button>
      </div>
    </div>
  )
}

function SeasonPanel({ player, stats, selectedTeamId }: { player: PlayerDto; stats: PlayerTeamStatsDto[]; selectedTeamId: number | 'all' | null }) {
  const [showTotals, setShowTotals] = useState(false)
  const [shots, setShots] = useState<ShotDto[]>([])

  const selected = useMemo(() => {
    if (selectedTeamId !== null && selectedTeamId !== 'all') return stats.find((s) => s.teamId === selectedTeamId) ?? null
    return stats[0] ?? null
  }, [stats, selectedTeamId])

  useEffect(() => {
    if (!selected) {
      setShots([])
      return
    }
    api.get<ShotDto[]>(`/shots/player/${player.id}/team/${selected.teamId}`).then((res) => setShots(res.data)).catch(() => setShots([]))
  }, [player.id, selected])

  if (stats.length === 0) return <p>No teams yet - add your player to a team on the Players page.</p>
  if (!selected) return <p>Pick a specific team above to see its season stats.</p>

  return (
    <div>
      <div className="stats-toolbar">
        <div className="game-type-toggle">
          <button type="button" className={`toggle-option ${!showTotals ? 'active' : ''}`} onClick={() => setShowTotals(false)}>
            Averages
          </button>
          <button type="button" className={`toggle-option ${showTotals ? 'active' : ''}`} onClick={() => setShowTotals(true)}>
            Totals
          </button>
        </div>
      </div>

      <div className="stats-tables">
        <div className="stats-card-enhanced">
          <div className="player-header-enhanced">
            <div className="player-info">
              <div className="jersey">{selected.jerseyNumber}</div>
              <div>
                <h3>{selected.playerName}</h3>
                <p>{selected.position} · {selected.teamName}</p>
              </div>
            </div>
            <div className="games-badge">{selected.gamesPlayed} Games</div>
          </div>

          <div className="primary-stats">
            <h4>{showTotals ? 'Totals' : 'Key Averages'}</h4>
            <div className="stats-grid-enhanced">
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? selected.totalPoints : selected.pointsPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'PTS' : 'PPG'}</span>
                <span className="stat-detail">{showTotals ? `${selected.pointsPerGame.toFixed(1)}/game` : `${selected.totalPoints} total`}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? selected.totalRebounds : selected.reboundsPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'REB' : 'RPG'}</span>
                <span className="stat-detail">{showTotals ? `${selected.reboundsPerGame.toFixed(1)}/game` : `${selected.totalRebounds} total`}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? selected.totalAssists : selected.assistsPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'AST' : 'APG'}</span>
                <span className="stat-detail">{showTotals ? `${selected.assistsPerGame.toFixed(1)}/game` : `${selected.totalAssists} total`}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? selected.totalSteals : selected.stealsPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'STL' : 'STL/G'}</span>
                <span className="stat-detail">{showTotals ? `${selected.stealsPerGame.toFixed(1)}/game` : `${selected.totalSteals} total`}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? selected.totalBlocks : selected.blocksPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'BLK' : 'BLK/G'}</span>
                <span className="stat-detail">{showTotals ? `${selected.blocksPerGame.toFixed(1)}/game` : `${selected.totalBlocks} total`}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? selected.totalTurnovers : selected.turnoversPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'TO' : 'TO/G'}</span>
                <span className="stat-detail">{showTotals ? `${selected.turnoversPerGame.toFixed(1)}/game` : `${selected.totalTurnovers} total`}</span>
              </div>
            </div>
          </div>

          <div className="shooting-stats">
            <h4>Shooting Percentages</h4>
            <div className="percentage-bars">
              <div className="percentage-item">
                <div className="percentage-label">Field Goal %</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${selected.fieldGoalPercentage}%` }} /></div>
                <span className="percentage-value">{selected.fieldGoalPercentage}%</span>
              </div>
              <div className="percentage-item">
                <div className="percentage-label">3-Point %</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${selected.threePointPercentage}%` }} /></div>
                <span className="percentage-value">{selected.threePointPercentage}%</span>
              </div>
              <div className="percentage-item">
                <div className="percentage-label">Free Throw %</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${selected.freeThrowPercentage}%` }} /></div>
                <span className="percentage-value">{selected.freeThrowPercentage}%</span>
              </div>
            </div>
          </div>

          <div className="season-chart-section">
            <h4>🎯 {selected.teamName} Shot Chart ({shots.length} shots)</h4>
            {shots.length === 0 ? (
              <p className="no-shots-note">No shots logged for this team yet.</p>
            ) : (
              <CourtShotChart shots={shots} interactive={false} />
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
