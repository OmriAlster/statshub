import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import type { GameDto, SharedPlayerDto, SharedTeamDto } from '../api/types'
import CourtShotChart from '../components/CourtShotChart'
import SegmentedControl from '../components/SegmentedControl'
import TeamCrest from '../components/TeamCrest'

export default function SharedPlayerView() {
  const { token, gameId } = useParams<{ token: string; gameId?: string }>()
  const [data, setData] = useState<SharedPlayerDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selectedTeamId, setSelectedTeamId] = useState<number | null>(null)

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

  const viewedGame = gameId ? data.games.find((g) => String(g.id) === gameId) : null

  if (viewedGame) {
    return (
      <div className="shared-view">
        <div className="page-container">
          <Link to={`/share/${token}`} className="back-link">← Back to profile</Link>
          <SharedGame game={viewedGame} />
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
          <SharedGame game={data.game} />
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

            {selectedTeam && (
              <div className="season-summary">
                <div className="summary-stat">
                  <span className="summary-value">{selectedTeam.gamesPlayed}</span>
                  <span className="summary-label">Games Played</span>
                </div>
                <div className="summary-stat">
                  <span className="summary-value">{selectedTeam.pointsPerGame.toFixed(1)}</span>
                  <span className="summary-label">PPG</span>
                </div>
                <div className="summary-stat">
                  <span className="summary-value">{selectedTeam.reboundsPerGame.toFixed(1)}</span>
                  <span className="summary-label">RPG</span>
                </div>
                <div className="summary-stat">
                  <span className="summary-value">{selectedTeam.assistsPerGame.toFixed(1)}</span>
                  <span className="summary-label">APG</span>
                </div>
              </div>
            )}

            <SharedGamesTable games={teamGames} token={token!} />
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

function SharedGamesTable({ games, token }: { games: GameDto[]; token: string }) {
  const { completedGames, averages } = useMemo(() => {
    const completedGames = games
      .filter((g) => g.status === 'Completed')
      .sort((a, b) => new Date(b.gameDate).getTime() - new Date(a.gameDate).getTime())
    const gamesWithStats = completedGames.filter((g) => g.playerStats.length > 0)
    const avg = (pick: (g: GameDto) => number) =>
      gamesWithStats.length ? gamesWithStats.reduce((sum, g) => sum + pick(g), 0) / gamesWithStats.length : 0
    const averages = gamesWithStats.length
      ? {
          pts: avg((g) => g.playerStats[0]?.totalPoints ?? 0).toFixed(1),
          reb: avg((g) => g.playerStats[0]?.totalRebounds ?? 0).toFixed(1),
          ast: avg((g) => g.playerStats[0]?.assists ?? 0).toFixed(1),
        }
      : null
    return { completedGames, averages }
  }, [games])

  const upcomingGames = useMemo(
    () =>
      games
        .filter((g) => g.status !== 'Completed')
        .sort((a, b) => new Date(a.gameDate).getTime() - new Date(b.gameDate).getTime()),
    [games]
  )

  return (
    <>
      {completedGames.length > 0 && (
        <div className="games-table-wrap">
          <table className="games-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Opponent</th>
                <th>Type</th>
                <th className="num">Score</th>
                <th className="num">Pts</th>
                <th className="num">Reb</th>
                <th className="num">Ast</th>
              </tr>
            </thead>
            <tbody>
              {completedGames.map((game) => {
                const stats = game.playerStats[0]
                const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)
                return (
                  <tr key={game.id}>
                    <td>
                      <Link to={`/share/${token}/games/${game.id}`} className="games-table-date-link">
                        {new Date(game.gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}
                      </Link>
                    </td>
                    <td>
                      {game.isHomeGame != null && <span>{game.isHomeGame ? '🏠' : '✈️'} </span>}
                      <Link to={`/share/${token}/games/${game.id}`}>{game.opponentName}</Link>
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
                    <td className={`num ${won ? 'win' : 'loss'}`}>{game.teamScore}&ndash;{game.opponentScore}</td>
                    <td className="num">{stats?.totalPoints ?? '-'}</td>
                    <td className="num">{stats?.totalRebounds ?? '-'}</td>
                    <td className="num">{stats?.assists ?? '-'}</td>
                  </tr>
                )
              })}
              {averages && (
                <tr className="avg-row">
                  <td colSpan={4}>Avg</td>
                  <td className="num">{averages.pts}</td>
                  <td className="num">{averages.reb}</td>
                  <td className="num">{averages.ast}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {upcomingGames.length > 0 && (
        <div className="recent-games-section">
          <h3>Upcoming</h3>
          <div className="mini-game-list">
            {upcomingGames.map((game) => (
              <Link className="mini-game" key={game.id} to={`/share/${token}/games/${game.id}`}>
                <div className="game-date">
                  {new Date(game.gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}
                </div>
                <div className="game-info">
                  <div>
                    {game.isHomeGame != null && <span>{game.isHomeGame ? '🏠' : '✈️'} </span>}
                    vs {game.opponentName}
                    <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>{game.gameType}</span>
                  </div>
                </div>
                <div className="game-result">{game.status}</div>
              </Link>
            ))}
          </div>
        </div>
      )}
    </>
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
      <p>
        {game.teamName} • {new Date(game.gameDate).toLocaleDateString()} •{' '}
        {game.isHomeGame != null && <>{game.isHomeGame ? '🏠' : '✈️'} </>}
        {game.location}
      </p>
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
      {stats && (
        <div className="breakdown">
          2P: {stats.fieldGoalsMade}/{stats.fieldGoalsAttempted} | 3P: {stats.threePointersMade}/{stats.threePointersAttempted} | FT: {stats.freeThrowsMade}/{stats.freeThrowsAttempted}
        </div>
      )}
      {stats && stats.shots && stats.shots.length > 0 && (
        <div className="season-chart-section">
          <h4>🎯 Shot Chart ({stats.shots.length} shots)</h4>
          <CourtShotChart shots={stats.shots} interactive={false} />
        </div>
      )}
      {!stats && game.status !== 'Completed' && <p>This game hasn't been played yet.</p>}
    </div>
  )
}
