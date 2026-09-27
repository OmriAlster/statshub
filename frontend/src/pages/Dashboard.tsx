import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { useLiveGameOverlay } from '../live/LiveGameContext'
import type { GameDto, IbbaLinkStatusDto, PlayerDto, PlayerTeamStatsDto, SeasonDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import IbbaBadge from '../components/IbbaBadge'
import StandingsModal from '../components/StandingsModal'
import TeamCrest from '../components/TeamCrest'
import { formatGameDateOnly, formatGameTime } from '../utils/formatGameDate'

interface PlayerCard {
  player: PlayerDto
  teamStats: PlayerTeamStatsDto[]
  ibba: IbbaLinkStatusDto | null
  gamesByTeam: Record<number, GameDto[]>
}

interface UpcomingGame {
  game: GameDto
  // Every one of your players on this game - two siblings on the same team
  // share one row instead of listing the game twice.
  players: PlayerDto[]
}

const UPCOMING_PREVIEW_COUNT = 5

// A manual game nobody entered a score for stays "Upcoming" after it's
// played - keep it listed only for a few hours past tip-off, not forever.
const UPCOMING_GRACE_MS = 3 * 60 * 60 * 1000

function collectUpcomingGames(cards: PlayerCard[]): UpcomingGame[] {
  const byId = new Map<number, UpcomingGame>()
  const cutoff = Date.now() - UPCOMING_GRACE_MS
  for (const { player, gamesByTeam } of cards) {
    for (const game of Object.values(gamesByTeam).flat()) {
      const isLive = game.status === 'In Progress'
      const isUpcoming = game.status === 'Upcoming' && new Date(game.gameDate).getTime() >= cutoff
      if (!isLive && !isUpcoming) continue
      const entry = byId.get(game.id)
      if (entry) {
        if (!entry.players.some((p) => p.id === player.id)) entry.players.push(player)
      } else {
        byId.set(game.id, { game, players: [player] })
      }
    }
  }
  // Live games first, then soonest first.
  return [...byId.values()].sort((a, b) => {
    const liveA = a.game.status === 'In Progress' ? 0 : 1
    const liveB = b.game.status === 'In Progress' ? 0 : 1
    if (liveA !== liveB) return liveA - liveB
    return new Date(a.game.gameDate).getTime() - new Date(b.game.gameDate).getTime()
  })
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
  const [standingsFor, setStandingsFor] = useState<{ leagueUrl: string; leagueName: string; teamName: string; teamUrl: string } | null>(null)
  const [showAllUpcoming, setShowAllUpcoming] = useState(false)
  const upcomingGames = useMemo(() => collectUpcomingGames(players), [players])

  useEffect(() => {
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isPlayerRole])

  // A live game started or finished from the live widget on top of this
  // page - the last/next game and averages here should reflect it.
  const { liveGameVersion } = useLiveGameOverlay()
  const seenLiveGameVersion = useRef(liveGameVersion)
  useEffect(() => {
    if (liveGameVersion === seenLiveGameVersion.current) return
    seenLiveGameVersion.current = liveGameVersion
    load(true)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [liveGameVersion])

  // quiet: no "Loading..." flash over what's already shown.
  const load = async (quiet = false) => {
    try {
      if (!quiet) setLoading(true)

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
      if (!quiet) setError('Could not load your dashboard. Is the backend running?')
    } finally {
      if (!quiet) setLoading(false)
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
            <Link className="player-card-head" to={`/stats/${player.id}`}>
              {player.profilePictureUrl ? (
                <img className="player-card-avatar" src={player.profilePictureUrl} alt="" />
              ) : (
                <div className="player-card-avatar">{player.firstName[0]}{player.lastName[0]}</div>
              )}
              <div>
                <h3>
                  {player.firstName} {player.lastName}
                  {ibba && <IbbaBadge />}
                </h3>
                <p className="player-card-role">{player.position || 'Player'}</p>
              </div>
            </Link>
            {teamStats.length > 0 ? (
              <div className="player-card-teams">
                {teamStats.map((s) => {
                  const ibbaTeam = ibba?.teams.find((t) => t.linkedTeamId === s.teamId)
                  const { last, next } = lastAndNextGame(gamesByTeam[s.teamId] ?? [])
                  return (
                    <div className="player-card-team-row" key={s.teamId}>
                      <div className="pctr-top">
                        <div className="pctr-team">
                          <TeamCrest
                            logoUrl={ibbaTeam?.teamLogoUrl}
                            jerseyNumber={s.jerseyNumber}
                            showIbbaMark={!!ibbaTeam}
                            size="sm"
                            onClick={ibbaTeam?.ibbaLeagueUrl ? () => setStandingsFor({ leagueUrl: ibbaTeam.ibbaLeagueUrl!, leagueName: ibbaTeam.ibbaLeagueName ?? '', teamName: s.teamName, teamUrl: ibbaTeam.teamUrl }) : undefined}
                            title={ibbaTeam?.ibbaLeagueUrl ? 'View standings' : undefined}
                          />
                          <div style={{ minWidth: 0 }}>
                            <div className="pctr-team-name team-name-clamp" title={s.teamName}>{s.teamName}</div>
                            {ibbaTeam?.ibbaLeagueName && <div className="pctr-league" dir="rtl">{ibbaTeam.ibbaLeagueName}</div>}
                          </div>
                        </div>
                        {!!ibbaTeam?.position && ibbaTeam.position > 0 && (
                          <label className="pos-pill" onClick={() => setStandingsFor({ leagueUrl: ibbaTeam.ibbaLeagueUrl!, leagueName: ibbaTeam.ibbaLeagueName ?? '', teamName: s.teamName, teamUrl: ibbaTeam.teamUrl })}>
                            <svg className="icon"><use href="#i-trophy" /></svg>
                            {ibbaTeam.position}{ibbaTeam.position === 1 ? 'st' : ibbaTeam.position === 2 ? 'nd' : ibbaTeam.position === 3 ? 'rd' : 'th'} of {ibbaTeam.totalTeams}
                          </label>
                        )}
                      </div>
                      <div className="pctr-stats">
                        <div><b>{s.pointsPerGame}</b><span>PPG</span></div>
                        <div><b>{s.reboundsPerGame}</b><span>RPG</span></div>
                        <div><b>{s.assistsPerGame}</b><span>APG</span></div>
                      </div>
                      {(last || next) && (
                        <div className="glance-grid">
                          <div className="glance-block">
                            <span className="glance-label">Last</span>
                            {last ? (
                              <>
                                <div className="glance-line opponent-cell">
                                  <span>{last.isHomeGame === false ? '✈️' : '🏠'}</span>
                                  {last.opponentLogoUrl && <img className="opponent-logo-sm" src={last.opponentLogoUrl} alt="" />}
                                  <span className="truncate">{last.opponentName}</span>
                                </div>
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
                                <div className="glance-line opponent-cell">
                                  <span>{next.isHomeGame === false ? '✈️' : '🏠'}</span>
                                  {next.opponentLogoUrl && <img className="opponent-logo-sm" src={next.opponentLogoUrl} alt="" />}
                                  <span className="truncate">{next.opponentName}</span>
                                </div>
                                <span className="glance-score upcoming">
                                  {formatGameDateOnly(next.gameDate)}
                                </span>
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
            <Link className="view-link" to={`/stats/${player.id}`}>View Stats →</Link>
          </div>
        ))}
      </div>

      <section className="upcoming-card">
        <h3 className="upcoming-title">
          <svg className="icon"><use href="#i-calendar" /></svg> Upcoming Games
          {upcomingGames.length > 0 && <span className="upcoming-count">{upcomingGames.length}</span>}
        </h3>
        {upcomingGames.length === 0 ? (
          <p className="upcoming-empty">No upcoming games scheduled.</p>
        ) : (
          <>
            <ul className={`upcoming-list ${showAllUpcoming ? 'expanded' : ''}`}>
              {(showAllUpcoming ? upcomingGames : upcomingGames.slice(0, UPCOMING_PREVIEW_COUNT)).map(({ game, players: gamePlayers }) => {
                const isLive = game.status === 'In Progress'
                const date = new Date(game.gameDate)
                return (
                  <li key={game.id}>
                    <Link className={`upcoming-row ${isLive ? 'live' : ''}`} to={`/games/${game.id}?playerId=${gamePlayers[0].id}`}>
                      <div className="upcoming-date-tile" aria-hidden="true">
                        <span className="tile-month">{date.toLocaleDateString('en-US', { month: 'short' })}</span>
                        <span className="tile-day">{date.getDate()}</span>
                        <span className="tile-weekday">{date.toLocaleDateString('en-US', { weekday: 'short' })}</span>
                      </div>
                      <div className="upcoming-matchup">
                        <div className="upcoming-opponent-line">
                          {game.opponentLogoUrl && <img className="opponent-logo-sm" src={game.opponentLogoUrl} alt="" />}
                          <span className="upcoming-opponent">{game.opponentName}</span>
                        </div>
                        <div className="upcoming-meta">
                          {game.isHomeGame != null && <span>{game.isHomeGame ? '🏠 Home' : '✈️ Away'}</span>}
                          <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>
                            {game.gameType}
                            {game.isFromIbba && <img className="type-chip-ibba" src="/icons/ibba-logo.png" alt="" title="Synced from IBBA" />}
                          </span>
                          <span className="upcoming-team">
                            {game.teamName}
                            {players.length > 1 && ` · ${gamePlayers.map((p) => p.firstName).join(', ')}`}
                          </span>
                        </div>
                      </div>
                      <div className="upcoming-when">
                        {isLive ? (
                          <span className="upcoming-live"><span className="fab-live-dot" /> Live</span>
                        ) : (
                          <span className="upcoming-time">{formatGameTime(game.gameDate)}</span>
                        )}
                      </div>
                    </Link>
                  </li>
                )
              })}
            </ul>
            {upcomingGames.length > UPCOMING_PREVIEW_COUNT && (
              <button className="upcoming-more" onClick={() => setShowAllUpcoming((v) => !v)} aria-expanded={showAllUpcoming}>
                {showAllUpcoming ? 'See less' : `See more (${upcomingGames.length - UPCOMING_PREVIEW_COUNT})`}
                <svg className={`icon ${showAllUpcoming ? 'flip' : ''}`}><use href="#i-chevron" /></svg>
              </button>
            )}
          </>
        )}
      </section>

      {standingsFor && (
        <StandingsModal
          leagueUrl={standingsFor.leagueUrl}
          leagueName={standingsFor.leagueName}
          highlightTeamUrl={standingsFor.teamUrl}
          highlightTeamName={standingsFor.teamName}
          onClose={() => setStandingsFor(null)}
        />
      )}
    </div>
  )
}
