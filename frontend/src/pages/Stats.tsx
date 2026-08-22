import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { GameDto, IbbaLinkStatusDto, PlayerDto, PlayerTeamStatsDto, ShotDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import CourtShotChart from '../components/CourtShotChart'
import GameStatusBadge from '../components/GameStatusBadge'
import SegmentedControl from '../components/SegmentedControl'
import TeamCrest from '../components/TeamCrest'

export default function Stats() {
  const { user } = useAuth()
  const isPlayerRole = user?.role === 'Player'

  const [tab, setTab] = useState<'games' | 'season'>('games')
  const [players, setPlayers] = useState<PlayerDto[]>([])
  const [selectedPlayerId, setSelectedPlayerId] = useState<number | ''>('')
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
      if (list.length > 0) setSelectedPlayerId(list[0].id)
    } catch {
      setError('Could not load players.')
    } finally {
      setPlayersLoading(false)
    }
  }

  const selectedPlayer = players.find((p) => p.id === selectedPlayerId)

  return (
    <div className="page-container">
      <h2>📈 Stats</h2>

      <SegmentedControl
        className="stats-tab-switch"
        options={[
          { value: 'games', label: '📅 Games' },
          { value: 'season', label: '📊 Season' },
        ]}
        value={tab}
        onChange={setTab}
      />

      {error && <p className="error">{error}</p>}

      {playersLoading ? (
        <p>Loading...</p>
      ) : players.length === 0 ? (
        <p>No players yet.</p>
      ) : (
        <>
          {players.length > 1 && (
            <div className="stats-player-switch">
              <SegmentedControl
                options={players.map((p) => ({ value: p.id, label: `${p.firstName} ${p.lastName}` }))}
                value={selectedPlayerId as number}
                onChange={setSelectedPlayerId}
              />
            </div>
          )}

          {tab === 'games' && selectedPlayer && <GamesTab player={selectedPlayer} />}
          {tab === 'season' && selectedPlayer && <SeasonTab player={selectedPlayer} />}
        </>
      )}
    </div>
  )
}

function GamesTab({ player }: { player: PlayerDto }) {
  const [games, setGames] = useState<GameDto[]>([])
  const [ibba, setIbba] = useState<IbbaLinkStatusDto | null>(null)
  const [selectedTeamId, setSelectedTeamId] = useState<number | 'all'>('all')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    load(player.id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [player.id])

  const load = async (playerId: number) => {
    try {
      setLoading(true)
      const [{ data }, ibbaData] = await Promise.all([
        api.get<GameDto[]>(`/games/player/${playerId}`),
        api.get<IbbaLinkStatusDto>(`/players/${playerId}/ibba`).then((res) => res.data).catch(() => null),
      ])
      setGames(data)
      setIbba(ibbaData)
      setSelectedTeamId('all')
      setError(null)
    } catch {
      setError('Could not load games.')
    } finally {
      setLoading(false)
    }
  }

  const teamOptions = useMemo(() => Array.from(new Map(games.map((g) => [g.teamId, g.teamName])).entries()), [games])

  // Per-team crest (from IBBA, when that team is linked) and jersey number
  // (this player wears a different number per team) - so a row's Team badge
  // is a real identity, not decoration.
  const teamMeta = useMemo(() => {
    const meta: Record<number, { logoUrl?: string | null; jerseyNumber?: number | null; isIbba: boolean }> = {}
    for (const t of player.teams ?? []) {
      meta[t.id] = { jerseyNumber: t.jerseyNumber, isIbba: false }
    }
    for (const t of ibba?.teams ?? []) {
      if (t.linkedTeamId != null) {
        meta[t.linkedTeamId] = { ...meta[t.linkedTeamId], logoUrl: t.teamLogoUrl, isIbba: true }
      }
    }
    return meta
  }, [player.teams, ibba])

  const { filteredGames, gamesWithStats, wins, losses, ppg, averages } = useMemo(() => {
    const filteredGames = selectedTeamId === 'all' ? games : games.filter((g) => g.teamId === selectedTeamId)
    const completedGames = filteredGames.filter((g) => g.status === 'Completed')
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

    return { filteredGames, gamesWithStats, wins, losses, ppg, averages }
  }, [games, selectedTeamId])

  return (
    <div>
      {teamOptions.length > 1 && (
        <div className="stats-team-switch">
          <SegmentedControl
            options={[{ value: 'all' as const, label: 'All Teams' }, ...teamOptions.map(([id, name]) => ({ value: id, label: name }))]}
            value={selectedTeamId}
            onChange={setSelectedTeamId}
          />
        </div>
      )}

      {error && <p className="error">{error}</p>}

      <div className="season-summary">
        <div className="summary-stat">
          <span className="summary-value">{gamesWithStats.length}</span>
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

      {loading && <p>Loading games...</p>}

      {!loading && filteredGames.length === 0 ? (
        <p>No games found yet. Start a live game to record one.</p>
      ) : (
        <div className="games-table-wrap">
          <table className="games-table">
            <thead>
              <tr>
                <th>Team</th>
                <th>Date</th>
                <th>Opponent</th>
                <th>Type</th>
                <th className="num">Score</th>
                <th className="num">Pts</th>
                <th className="num col-optional">2PT</th>
                <th className="num col-optional">3PT</th>
                <th className="num col-optional">FT</th>
                <th className="num col-optional">Reb</th>
                <th className="num col-optional">Ast</th>
                <th className="num col-optional">Stl</th>
                <th className="num col-optional">Blk</th>
                <th className="num col-optional">TO</th>
              </tr>
            </thead>
            <tbody>
              {filteredGames.map((game) => {
                const stats = game.playerStats[0]
                const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)
                const meta = teamMeta[game.teamId]
                return (
                  <tr key={game.id} className={game.status !== 'Completed' ? 'upcoming-row' : ''}>
                    <td>
                      <TeamCrest logoUrl={meta?.logoUrl} jerseyNumber={meta?.jerseyNumber} showIbbaMark={meta?.isIbba} size="sm" title={game.teamName} />
                    </td>
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
                    {game.status === 'Completed' ? (
                      <>
                        <td className={`num ${won ? 'win' : 'loss'}`}>
                          {game.teamScore}&ndash;{game.opponentScore}
                        </td>
                        <td className="num">{stats?.totalPoints ?? '-'}</td>
                        <td className="num col-optional">{stats ? `${stats.fieldGoalsMade}/${stats.fieldGoalsAttempted}` : '-'}</td>
                        <td className="num col-optional">{stats ? `${stats.threePointersMade}/${stats.threePointersAttempted}` : '-'}</td>
                        <td className="num col-optional">{stats ? `${stats.freeThrowsMade}/${stats.freeThrowsAttempted}` : '-'}</td>
                        <td className="num col-optional">{stats?.totalRebounds ?? '-'}</td>
                        <td className="num col-optional">{stats?.assists ?? '-'}</td>
                        <td className="num col-optional">{stats?.steals ?? '-'}</td>
                        <td className="num col-optional">{stats?.blocks ?? '-'}</td>
                        <td className="num col-optional">{stats?.turnovers ?? '-'}</td>
                      </>
                    ) : (
                      <td className="games-table-status" colSpan={10}><GameStatusBadge status={game.status} /></td>
                    )}
                  </tr>
                )
              })}
              {averages && (
                <tr className="avg-row">
                  <td colSpan={4}>Avg</td>
                  <td className="num">&mdash;</td>
                  <td className="num">{averages.pts}</td>
                  <td className="num col-optional">{averages.fgm}/{averages.fga}</td>
                  <td className="num col-optional">{averages.tpm}/{averages.tpa}</td>
                  <td className="num col-optional">{averages.ftm}/{averages.fta}</td>
                  <td className="num col-optional">{averages.reb}</td>
                  <td className="num col-optional">{averages.ast}</td>
                  <td className="num col-optional">{averages.stl}</td>
                  <td className="num col-optional">{averages.blk}</td>
                  <td className="num col-optional">{averages.to}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function SeasonTab({ player }: { player: PlayerDto }) {
  const [stats, setStats] = useState<PlayerTeamStatsDto[]>([])
  const [selectedTeamId, setSelectedTeamId] = useState<number | ''>('')
  const [showTotals, setShowTotals] = useState(false)
  const [shots, setShots] = useState<ShotDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    loadStats(player.id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [player.id])

  useEffect(() => {
    if (selectedTeamId) loadShots(player.id, selectedTeamId)
    else setShots([])
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [player.id, selectedTeamId])

  const loadStats = async (playerId: number) => {
    try {
      setLoading(true)
      const { data } = await api.get<PlayerTeamStatsDto[]>(`/gamestats/player/${playerId}`)
      setStats(data)
      setSelectedTeamId(data.length > 0 ? data[0].teamId : '')
      setError(null)
    } catch {
      setError('Could not load stats.')
    } finally {
      setLoading(false)
    }
  }

  const loadShots = async (playerId: number, teamId: number) => {
    try {
      const { data } = await api.get<ShotDto[]>(`/shots/player/${playerId}/team/${teamId}`)
      setShots(data)
    } catch {
      setShots([])
    }
  }

  if (loading) return <p>Loading...</p>

  const selected = stats.find((s) => s.teamId === selectedTeamId)

  return (
    <div>
      {error && <p className="error">{error}</p>}

      {stats.length === 0 ? (
        <p>No teams yet - add your player to a team on the Players page.</p>
      ) : (
        <div className="stats-toolbar">
          <SegmentedControl
            options={stats.map((s) => ({ value: s.teamId, label: s.teamName }))}
            value={selectedTeamId as number}
            onChange={setSelectedTeamId}
          />
          <div className="game-type-toggle">
            <button type="button" className={`toggle-option ${!showTotals ? 'active' : ''}`} onClick={() => setShowTotals(false)}>
              Averages
            </button>
            <button type="button" className={`toggle-option ${showTotals ? 'active' : ''}`} onClick={() => setShowTotals(true)}>
              Totals
            </button>
          </div>
        </div>
      )}

      {selected && (
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
      )}
    </div>
  )
}
