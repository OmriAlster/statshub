import type { ReactNode } from 'react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import type { GameDto, ShotDto, SharedPlayerDto, SharedTeamDto } from '../api/types'
import CourtShotChart from '../components/CourtShotChart'
import GameDetailView from '../components/GameDetailView'
import GameStatusBadge from '../components/GameStatusBadge'
import SegmentedControl from '../components/SegmentedControl'
import TeamCrest from '../components/TeamCrest'
import { useElementVisible } from '../hooks/useElementVisible'
import { formatGameDateTime } from '../utils/formatGameDate'

export default function SharedPlayerView() {
  const { token, gameId } = useParams<{ token: string; gameId?: string }>()
  const [data, setData] = useState<SharedPlayerDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selectedTeamId, setSelectedTeamId] = useState<number | null>(null)
  const [tab, setTab] = useState<'stats' | 'schedule' | 'season'>('stats')

  const load = useCallback(async () => {
    if (!token) return
    try {
      const { data: shared } = await api.get<SharedPlayerDto>(`/share/${token}`)
      setData(shared)
      setSelectedTeamId((prev) => prev ?? shared.teams[0]?.teamId ?? null)
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
  // Covers both a single-game share token (`data.game`) and a specific game
  // being viewed from inside a full profile share (`gameId` route param).
  const viewedGameStatus = gameId ? data?.games.find((g) => String(g.id) === gameId)?.status : undefined
  const gameStatus = data?.game?.status ?? viewedGameStatus
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

  const viewedGame = gameId ? data.games.find((g) => String(g.id) === gameId) : null

  if (viewedGame) {
    return (
      <div className="shared-view">
        <div className="page-container">
          <Link to={`/share/${token}`} className="back-link">← Back to profile</Link>
          <GameDetailView game={viewedGame} />
          <p className="shared-footer">Shared via StatsHub 🏀</p>
        </div>
      </div>
    )
  }

  const selectedTeam = data.teams.find((t) => t.teamId === selectedTeamId) ?? data.teams[0] ?? null
  const teamGames = selectedTeam ? data.games.filter((g) => g.teamId === selectedTeam.teamId) : []

  return (
    <div className="shared-view">
      <div className="page-container">
        <div className="profile-top-row">
          {data.profilePictureUrl ? (
            <img className="avatar-lg" src={data.profilePictureUrl} alt="" />
          ) : (
            <div className="avatar-lg">{data.playerName.split(' ').map((n) => n[0]).slice(0, 2).join('')}</div>
          )}
          <div>
            <h2 style={{ margin: 0 }}>{data.playerName}</h2>
            <p style={{ margin: '0.15rem 0 0' }}>{data.position || 'Player'}</p>
          </div>
        </div>

        {data.game ? (
          <GameDetailView game={data.game} />
        ) : data.teams.length === 0 ? (
          <p>No teams yet.</p>
        ) : (
          <>
            {data.teams.length > 1 && (
              <div className="stats-team-switch">
                <SegmentedControl
                  options={data.teams.map((t) => ({
                    value: t.teamId,
                    label: (
                      <span className="team-switch-option">
                        {t.logoUrl ? <img src={t.logoUrl} alt="" /> : <span className="team-switch-option-fallback" />}
                        {t.teamName}
                      </span>
                    ),
                  }))}
                  value={selectedTeam?.teamId ?? data.teams[0].teamId}
                  onChange={setSelectedTeamId}
                />
              </div>
            )}

            {selectedTeam && <TeamMetaStrip team={selectedTeam} />}

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

            {tab === 'stats' && <SharedStatsPanel games={teamGames} />}
            {tab === 'schedule' && <SharedSchedulePanel games={teamGames} token={token!} />}
            {tab === 'season' && selectedTeam && <SharedSeasonPanel team={selectedTeam} games={teamGames} />}
          </>
        )}

        <p className="shared-footer">Shared via StatsHub 🏀</p>
      </div>
    </div>
  )
}

function TeamMetaStrip({ team }: { team: SharedTeamDto }) {
  return (
    <div className="team-meta-strip">
      <TeamCrest logoUrl={team.logoUrl} jerseyNumber={team.jerseyNumber} showIbbaMark={team.isIbba} />
      <div>
        <div className="pctr-team-name">{team.teamName}</div>
        {team.leagueName && (
          <div className="league-chip" style={{ marginTop: '0.3rem' }}>
            <svg className="icon"><use href="#i-trophy" /></svg>
            <span dir="rtl">{team.leagueName}</span>
            {team.standingPosition && ` · ${team.standingPosition} of ${team.standingTotalTeams}`}
          </div>
        )}
      </div>
    </div>
  )
}

// Mirrors the authenticated Stats tab: completed games only, with an
// averages row - same table shape, just read-only.
function SharedStatsPanel({ games }: { games: GameDto[] }) {
  const { completedGames, wins, losses, ppg, averages } = useMemo(() => {
    const completedGames = games
      .filter((g) => g.status === 'Completed')
      .sort((a, b) => new Date(b.gameDate).getTime() - new Date(a.gameDate).getTime())
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

  const [wrapRef, wrapVisible] = useElementVisible<HTMLDivElement>()
  const [footRef, footVisible] = useElementVisible<HTMLTableSectionElement>()
  const showFloatingAvg = !!averages && wrapVisible && !footVisible

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
        <div className="games-table-wrap" ref={wrapRef}>
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
                  <GameRow key={game.id} game={game}>
                    <td className={`num ${won ? 'win' : 'loss'}`}>{game.teamScore}&ndash;{game.opponentScore}</td>
                    <td className="num">{stats?.totalPoints ?? '-'}</td>
                    <td className="num">{stats ? `${stats.fieldGoalsMade}/${stats.fieldGoalsAttempted}` : '-'}</td>
                    <td className="num">{stats ? `${stats.threePointersMade}/${stats.threePointersAttempted}` : '-'}</td>
                    <td className="num">{stats ? `${stats.freeThrowsMade}/${stats.freeThrowsAttempted}` : '-'}</td>
                    <td className="num">{stats?.totalRebounds ?? '-'}</td>
                    <td className="num">{stats?.assists ?? '-'}</td>
                    <td className="num">{stats?.steals ?? '-'}</td>
                    <td className="num">{stats?.blocks ?? '-'}</td>
                    <td className="num">{stats?.turnovers ?? '-'}</td>
                  </GameRow>
                )
              })}
            </tbody>
            {averages && (
              <tfoot ref={footRef}>
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
              </tfoot>
            )}
          </table>
        </div>
      )}

      {showFloatingAvg && averages && (
        <div className="floating-avg-bar">
          <span className="floating-avg-label">Avg</span>
          <div className="floating-avg-stats">
            <div><b>{averages.pts}</b><span>PTS</span></div>
            <div><b>{averages.reb}</b><span>REB</span></div>
            <div><b>{averages.ast}</b><span>AST</span></div>
            <div><b>{averages.stl}</b><span>STL</span></div>
            <div><b>{averages.blk}</b><span>BLK</span></div>
          </div>
        </div>
      )}
    </div>
  )
}

// Mirrors the authenticated Schedule tab's table shape (every game, not just
// completed ones) minus the edit/delete actions column - view only.
function SharedSchedulePanel({ games, token }: { games: GameDto[]; token: string }) {
  const sorted = useMemo(
    () => [...games].sort((a, b) => new Date(a.gameDate).getTime() - new Date(b.gameDate).getTime()),
    [games]
  )

  if (sorted.length === 0) return <p>No games scheduled yet.</p>

  return (
    <div className="games-table-wrap">
      <table className="games-table">
        <thead>
          <tr>
            <th>Date</th>
            <th>Opponent</th>
            <th>Type</th>
            <th className="num">Result</th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((game) => {
            const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)
            return (
              <GameRow key={game.id} game={game} token={token}>
                {game.status === 'Completed' ? (
                  <td className={`num ${won ? 'win' : 'loss'}`}>{won ? 'W' : 'L'} {game.teamScore}&ndash;{game.opponentScore}</td>
                ) : (
                  <td className="games-table-status"><GameStatusBadge status={game.status} /></td>
                )}
              </GameRow>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function GameRow({ game, token, children }: { game: GameDto; token?: string; children: ReactNode }) {
  const { token: tokenFromRoute } = useParams<{ token: string }>()
  const shareToken = token ?? tokenFromRoute
  return (
    <tr className={game.status !== 'Completed' ? 'upcoming-row' : ''}>
      <td>
        <Link to={`/share/${shareToken}/games/${game.id}`}>
          {formatGameDateTime(game.gameDate)}
        </Link>
      </td>
      <td>
        {game.isHomeGame != null && <span title={game.isHomeGame ? 'Home' : 'Away'}>{game.isHomeGame ? '🏠' : '✈️'} </span>}
        <Link to={`/share/${shareToken}/games/${game.id}`}>{game.opponentName}</Link>
        {game.isFromIbba && (
          <img
            src="/icons/ibba-logo.png"
            alt=""
            title="Synced from IBBA"
            style={{ width: 12, height: 12, marginLeft: '0.35rem', verticalAlign: '-1px', borderRadius: 2 }}
          />
        )}
      </td>
      <td><span className={`game-type-badge ${game.gameType.toLowerCase()}`}>{game.gameType}</span></td>
      {children}
    </tr>
  )
}

// Mirrors the authenticated Season tab: averages/totals toggle, key stats,
// shooting percentages, and a shot chart - the chart is built client-side
// from shots already embedded on this team's games, since there's no
// separate authenticated shots-by-team endpoint available on a public link.
function SharedSeasonPanel({ team, games }: { team: SharedTeamDto; games: GameDto[] }) {
  const [showTotals, setShowTotals] = useState(false)

  const shots: ShotDto[] = useMemo(
    () => games.flatMap((g) => g.playerStats.flatMap((s) => s.shots ?? [])),
    [games]
  )

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
              <div className="jersey">{team.jerseyNumber}</div>
              <div>
                <h3>{team.playerName}</h3>
                <p>{team.position} · {team.teamName}</p>
              </div>
            </div>
            <div className="games-badge">{team.gamesPlayed} Games</div>
          </div>

          <div className="primary-stats">
            <h4>{showTotals ? 'Totals' : 'Key Averages'}</h4>
            <div className="stats-grid-enhanced">
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? team.totalPoints : team.pointsPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'PTS' : 'PPG'}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? team.totalRebounds : team.reboundsPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'REB' : 'RPG'}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? team.totalAssists : team.assistsPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'AST' : 'APG'}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? team.totalSteals : team.stealsPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'STL' : 'STL/G'}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? team.totalBlocks : team.blocksPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'BLK' : 'BLK/G'}</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{showTotals ? team.totalTurnovers : team.turnoversPerGame.toFixed(1)}</span>
                <span className="stat-label">{showTotals ? 'TO' : 'TO/G'}</span>
              </div>
            </div>
          </div>

          <div className="shooting-stats">
            <h4>Shooting Percentages</h4>
            <div className="percentage-bars">
              <div className="percentage-item">
                <div className="percentage-label">Field Goal %</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${team.fieldGoalPercentage}%` }} /></div>
                <span className="percentage-value">{team.fieldGoalPercentage}%</span>
              </div>
              <div className="percentage-item">
                <div className="percentage-label">3-Point %</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${team.threePointPercentage}%` }} /></div>
                <span className="percentage-value">{team.threePointPercentage}%</span>
              </div>
              <div className="percentage-item">
                <div className="percentage-label">Free Throw %</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${team.freeThrowPercentage}%` }} /></div>
                <span className="percentage-value">{team.freeThrowPercentage}%</span>
              </div>
            </div>
          </div>

          <div className="season-chart-section">
            <h4>🎯 {team.teamName} Shot Chart ({shots.length} shots)</h4>
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
