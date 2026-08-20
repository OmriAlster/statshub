import { useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../api/client'
import type { GameDto, GameType, PlayerDto, ShotDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import CourtShotChart, { type ChartShot } from '../components/CourtShotChart'
import { useLiveGameOverlay } from './LiveGameContext'

type EventType = 'FT_MAKE' | 'FT_MISS' | 'OREB' | 'DREB' | 'AST' | 'STL' | 'BLK' | 'TO' | 'FOUL'

interface GameEvent {
  id: string
  type: EventType
  quarter: number
  display: string
}

interface ActionLogEntry {
  kind: 'event' | 'shot'
  id: string | number
  at: number
}

interface ActiveGame {
  gameId: number
  gameStatsId: number
  playerId: number
  playerName: string
  jerseyNumber: number
  teamName: string
  gameType: GameType
  opponent: string
  gameDate: string
  currentQuarter: number
  events: GameEvent[]
  shots: ShotDto[]
  actionLog: ActionLogEntry[]
  minutesPlayed: number
  shareUrl?: string
}

const STORAGE_KEY_PREFIX = 'statshub_active_live_game'

const EVENT_LABELS: Record<EventType, string> = {
  FT_MAKE: 'FT Make',
  FT_MISS: 'FT Miss',
  OREB: 'Off. Rebound',
  DREB: 'Def. Rebound',
  AST: 'Assist',
  STL: 'Steal',
  BLK: 'Block',
  TO: 'Turnover',
  FOUL: 'Foul',
}

const EVENT_ICONS: Record<EventType, string> = {
  FT_MAKE: 'i-check',
  FT_MISS: 'i-x',
  OREB: 'i-reb',
  DREB: 'i-reb',
  AST: 'i-ast',
  STL: 'i-stl',
  BLK: 'i-blk',
  TO: 'i-to',
  FOUL: 'i-foul',
}

function computeStatsFromEvents(events: GameEvent[], minutesPlayed: number) {
  const count = (types: EventType[]) => events.filter((e) => types.includes(e.type)).length
  return {
    freeThrowsMade: count(['FT_MAKE']),
    freeThrowsAttempted: count(['FT_MAKE', 'FT_MISS']),
    offensiveRebounds: count(['OREB']),
    defensiveRebounds: count(['DREB']),
    assists: count(['AST']),
    steals: count(['STL']),
    blocks: count(['BLK']),
    turnovers: count(['TO']),
    fouls: count(['FOUL']),
    minutesPlayed,
  }
}

export default function LiveGameWidget() {
  const { user } = useAuth()
  const { overlayOpen, openOverlay, closeOverlay } = useLiveGameOverlay()
  const [players, setPlayers] = useState<PlayerDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [selectedPlayerId, setSelectedPlayerId] = useState<number | ''>('')
  const [selectedTeamId, setSelectedTeamId] = useState<number | ''>('')
  const [gameType, setGameType] = useState<GameType>('League')
  const [opponent, setOpponent] = useState('')
  const [location, setLocation] = useState('')
  const [gameDate, setGameDate] = useState(new Date().toISOString().split('T')[0])
  const [starting, setStarting] = useState(false)

  const [active, setActive] = useState<ActiveGame | null>(null)
  const [pendingShot, setPendingShot] = useState<{ x: number; y: number; value: 2 | 3 } | null>(null)
  const [loggingShot, setLoggingShot] = useState(false)
  const [sharing, setSharing] = useState(false)
  const [copyMessage, setCopyMessage] = useState<string | null>(null)
  const [ending, setEnding] = useState(false)
  const [finalTeamScore, setFinalTeamScore] = useState('')
  const [finalOpponentScore, setFinalOpponentScore] = useState('')

  const saveTimer = useRef<number | null>(null)

  // Scoped per signed-in account so one browser can't leak an in-progress
  // game between two parents (e.g. mom + dad on the same shared computer).
  const storageKey = user ? `${STORAGE_KEY_PREFIX}_${user.id}` : null

  useEffect(() => {
    localStorage.removeItem(STORAGE_KEY_PREFIX) // clean up the old unscoped key, if any
    if (!storageKey) return

    const saved = localStorage.getItem(storageKey)
    if (saved) {
      try {
        const parsed = JSON.parse(saved) as ActiveGame
        setActive({ ...parsed, minutesPlayed: parsed.minutesPlayed ?? 0 })
        api
          .get<ShotDto[]>(`/shots/gamestats/${parsed.gameStatsId}`)
          .then(({ data }) => setActive((prev) => (prev ? { ...prev, shots: data, actionLog: [] } : prev)))
          .catch(() => {})
      } catch {
        localStorage.removeItem(storageKey)
      }
    } else {
      setActive(null)
    }
    loadSetupData()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [storageKey])

  useEffect(() => {
    if (!storageKey) return
    if (active) {
      localStorage.setItem(storageKey, JSON.stringify(active))
    } else {
      localStorage.removeItem(storageKey)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, storageKey])

  const loadSetupData = async () => {
    try {
      setLoading(true)
      const { data } = await api.get<PlayerDto[]>('/players')
      setPlayers(data)
      if (data.length === 1) {
        setSelectedPlayerId(data[0].id)
        if (data[0].teams.length > 0) setSelectedTeamId(data[0].teams[0].id)
      }
      // Defensive check: an active game whose player isn't accessible to this
      // account shouldn't be shown (e.g. a stale/foreign entry).
      setActive((prev) => {
        if (prev && !data.some((p) => p.id === prev.playerId)) {
          if (storageKey) localStorage.removeItem(storageKey)
          return null
        }
        return prev
      })
      setError(null)
    } catch {
      setError('Could not load players. Is the backend running?')
    } finally {
      setLoading(false)
    }
  }

  const selectedPlayer = players.find((p) => p.id === selectedPlayerId)
  const playerTeams = selectedPlayer?.teams ?? []

  const selectPlayer = (playerId: number) => {
    setSelectedPlayerId(playerId)
    const player = players.find((p) => p.id === playerId)
    setSelectedTeamId(player && player.teams.length > 0 ? player.teams[0].id : '')
  }

  const startGame = async () => {
    if (!selectedPlayerId || !selectedTeamId || !opponent.trim()) {
      setError('Pick a player, a team, and enter the opponent name')
      return
    }
    const today = new Date().toISOString().split('T')[0]
    if (gameDate > today) {
      setError("Can't start a live game with a future date - a live game is happening right now.")
      return
    }
    setStarting(true)
    setError(null)
    try {
      const gameRes = await api.post<GameDto>('/games', {
        teamId: selectedTeamId,
        gameType,
        opponentName: opponent.trim(),
        gameDate: new Date(gameDate).toISOString(),
        location: location.trim(),
      })
      const [, statsRes] = await Promise.all([
        api.put(`/games/${gameRes.data.id}`, { status: 'In Progress' }),
        api.post('/gamestats', {
          gameId: gameRes.data.id,
          playerId: selectedPlayerId,
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
        }),
      ])

      const player = players.find((p) => p.id === selectedPlayerId)!
      const team = player.teams.find((t) => t.id === selectedTeamId)
      setActive({
        gameId: gameRes.data.id,
        gameStatsId: statsRes.data.id,
        playerId: player.id,
        playerName: `${player.firstName} ${player.lastName}`,
        jerseyNumber: player.jerseyNumber,
        teamName: team?.name ?? '',
        gameType,
        opponent: opponent.trim(),
        gameDate,
        currentQuarter: 1,
        events: [],
        shots: [],
        actionLog: [],
        minutesPlayed: 0,
      })
    } catch {
      setError('Could not start the game. Please try again.')
    } finally {
      setStarting(false)
    }
  }

  const persistStats = (events: GameEvent[], minutesPlayed: number, gameStatsId: number) => {
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    saveTimer.current = window.setTimeout(() => {
      api.put(`/gamestats/${gameStatsId}`, computeStatsFromEvents(events, minutesPlayed)).catch(() => {
        setError('Could not save the last stat - check your connection.')
      })
    }, 500)
  }

  const addEvent = (type: EventType) => {
    if (!active) return
    const newEvent: GameEvent = {
      id: `${Date.now()}-${Math.random()}`,
      type,
      quarter: active.currentQuarter,
      display: EVENT_LABELS[type],
    }
    const events = [...active.events, newEvent]
    const actionLog = [...active.actionLog, { kind: 'event' as const, id: newEvent.id, at: Date.now() }]
    setActive({ ...active, events, actionLog })
    persistStats(events, active.minutesPlayed, active.gameStatsId)
  }

  const removeEvent = (eventId: string) => {
    if (!active) return
    const events = active.events.filter((e) => e.id !== eventId)
    const actionLog = active.actionLog.filter((a) => !(a.kind === 'event' && a.id === eventId))
    setActive({ ...active, events, actionLog })
    persistStats(events, active.minutesPlayed, active.gameStatsId)
  }

  const updateMinutesPlayed = (minutes: number) => {
    if (!active) return
    const clamped = Math.max(0, Math.min(48, minutes))
    setActive({ ...active, minutesPlayed: clamped })
    persistStats(active.events, clamped, active.gameStatsId)
  }

  // Precompute event/shot breakdowns once per state change instead of
  // re-scanning the full arrays on every render (this widget re-renders on
  // every stat tap during a live game, and several UI spots read these).
  const eventStats = useMemo(() => {
    const counts: Record<EventType, number> = {
      FT_MAKE: 0, FT_MISS: 0, OREB: 0, DREB: 0, AST: 0, STL: 0, BLK: 0, TO: 0, FOUL: 0,
    }
    const byQuarter: Record<number, GameEvent[]> = {}
    for (const e of active?.events ?? []) {
      counts[e.type]++
      ;(byQuarter[e.quarter] ??= []).push(e)
    }
    return { counts, byQuarter }
  }, [active?.events])

  const shotStats = useMemo(() => {
    const byQuarter: Record<number, ShotDto[]> = {}
    let made2 = 0, att2 = 0, made3 = 0, att3 = 0, points = 0
    for (const s of active?.shots ?? []) {
      ;(byQuarter[s.quarter] ??= []).push(s)
      if (s.value === 2) {
        att2++
        if (s.made) made2++
      } else {
        att3++
        if (s.made) made3++
      }
      if (s.made) points += s.value
    }
    return { byQuarter, made2, att2, made3, att3, points }
  }, [active?.shots])

  // Last few taps, most recent first — surfaced in a compact strip near
  // the top of the mobile layout so a parent doesn't have to scroll past
  // the whole button grid to confirm what was just logged.
  const recentActions = useMemo(() => {
    if (!active) return []
    return active.actionLog
      .slice(-5)
      .reverse()
      .map((a) => {
        if (a.kind === 'shot') {
          const shot = active.shots.find((s) => s.id === a.id)
          if (!shot) return null
          return {
            key: `shot-${shot.id}`,
            icon: shot.made ? 'i-check' : 'i-x',
            label: `${shot.value}PT ${shot.made ? 'Make' : 'Miss'}`,
            positive: shot.made,
          }
        }
        const event = active.events.find((e) => e.id === a.id)
        if (!event) return null
        return {
          key: `event-${event.id}`,
          icon: EVENT_ICONS[event.type],
          label: EVENT_LABELS[event.type],
          positive: event.type === 'FT_MAKE',
        }
      })
      .filter((x): x is { key: string; icon: string; label: string; positive: boolean } => x !== null)
  }, [active])

  const getTotal = (types: EventType[]) => types.reduce((sum, t) => sum + eventStats.counts[t], 0)
  const getQuarterEvents = (quarter: number) => eventStats.byQuarter[quarter] ?? []
  const getQuarterShots = (quarter: number) => shotStats.byQuarter[quarter] ?? []

  const handleCourtTap = (x: number, y: number, value: 2 | 3) => {
    if (!active || loggingShot) return
    setPendingShot({ x, y, value })
  }

  const confirmShot = async (made: boolean) => {
    if (!active || !pendingShot) return
    setLoggingShot(true)
    try {
      const { data } = await api.post<ShotDto>('/shots', {
        gameStatsId: active.gameStatsId,
        quarter: active.currentQuarter,
        x: pendingShot.x,
        y: pendingShot.y,
        made,
        value: pendingShot.value,
      })
      setActive((prev) =>
        prev
          ? { ...prev, shots: [...prev.shots, data], actionLog: [...prev.actionLog, { kind: 'shot', id: data.id, at: Date.now() }] }
          : prev
      )
      setPendingShot(null)
    } catch {
      setError('Could not log that shot - check your connection.')
    } finally {
      setLoggingShot(false)
    }
  }

  const removeShot = async (id: number) => {
    if (!active) return
    try {
      await api.delete(`/shots/${id}`)
      setActive((prev) =>
        prev
          ? { ...prev, shots: prev.shots.filter((s) => s.id !== id), actionLog: prev.actionLog.filter((a) => !(a.kind === 'shot' && a.id === id)) }
          : prev
      )
    } catch {
      setError('Could not remove that shot.')
    }
  }

  const undoLastAction = () => {
    if (!active || active.actionLog.length === 0) return
    const last = active.actionLog[active.actionLog.length - 1]
    if (last.kind === 'event') removeEvent(last.id as string)
    else removeShot(last.id as number)
  }

  const shotsMade = (value: 2 | 3) => (value === 2 ? shotStats.made2 : shotStats.made3)
  const shotsAttempted = (value: 2 | 3) => (value === 2 ? shotStats.att2 : shotStats.att3)

  const totalPoints = shotStats.points + eventStats.counts.FT_MAKE
  const totalRebounds = eventStats.counts.OREB + eventStats.counts.DREB

  const chartShots: ChartShot[] = active ? active.shots.map((s) => ({ id: s.id, x: s.x, y: s.y, made: s.made })) : []

  const createShareLink = async () => {
    if (!active) return
    setSharing(true)
    try {
      const { data } = await api.post('/share', { playerId: active.playerId, gameId: active.gameId })
      const url = `${window.location.origin}/share/${data.token}`
      setActive({ ...active, shareUrl: url })
    } catch {
      setError('Could not create a share link.')
    } finally {
      setSharing(false)
    }
  }

  const copyShareLink = async () => {
    if (!active?.shareUrl) return
    try {
      await navigator.clipboard.writeText(active.shareUrl)
      setCopyMessage('Copied!')
      setTimeout(() => setCopyMessage(null), 2000)
    } catch {
      setCopyMessage(active.shareUrl)
    }
  }

  const confirmEndGame = async () => {
    if (!active) return
    if (finalTeamScore.trim() === '' || finalOpponentScore.trim() === '') {
      setError('Enter both final scores before finishing the game.')
      return
    }
    try {
      await api.put(`/games/${active.gameId}`, {
        status: 'Completed',
        teamScore: Number(finalTeamScore),
        opponentScore: Number(finalOpponentScore),
      })
      setActive(null)
      setEnding(false)
      setFinalTeamScore('')
      setFinalOpponentScore('')
      setOpponent('')
      closeOverlay()
    } catch {
      setError('Could not finalize the game. Please try again.')
    }
  }

  if (loading) return null

  return (
    <>
      {!overlayOpen && (
        <button className={`fab-live ${active ? 'is-live' : ''}`} onClick={openOverlay}>
          {active ? (
            <>
              <span className="fab-live-dot" />
              <span>
                Live &middot; <b>{totalPoints}</b> PTS &middot; Q{active.currentQuarter}
              </span>
            </>
          ) : (
            <span>
              <svg className="icon" style={{ width: 15, height: 15 }}><use href="#i-live" /></svg> Start Live Game
            </span>
          )}
        </button>
      )}

      <div className={`live-overlay ${overlayOpen ? 'open' : ''}`}>
        <div className="live-overlay-header">
          <button className="back-btn" onClick={closeOverlay}>
            ← Back
          </button>
          <div className="live-overlay-title">
            {active ? (
              <>
                <span className="live-title-tag">
                  <span className="fab-live-dot" /> Live
                </span>
                {active.playerName} #{active.jerseyNumber} vs {active.opponent}
              </>
            ) : (
              'Start Live Game'
            )}
          </div>
        </div>

        <div className="live-overlay-body">
          {error && <p className="error">{error}</p>}

          {!active ? (
            <div className="game-setup">
              {players.length === 0 ? (
                <p>You don't have any players yet. Add one on the Players page first.</p>
              ) : (
                <div className="setup-card">
                  <h3>Start New Game</h3>
                  <div className="setup-form">
                    <div>
                      <label>Player:</label>
                      <select value={selectedPlayerId} onChange={(e) => selectPlayer(Number(e.target.value))}>
                        <option value="">Select a player</option>
                        {players.map((p) => (
                          <option key={p.id} value={p.id}>
                            {p.firstName} {p.lastName}
                          </option>
                        ))}
                      </select>
                    </div>
                    {selectedPlayerId && (
                      <div>
                        <label>Team:</label>
                        {playerTeams.length === 0 ? (
                          <p className="setup-hint">
                            This player isn't on a team yet - add one from the Players page first.
                          </p>
                        ) : (
                          <select value={selectedTeamId} onChange={(e) => setSelectedTeamId(Number(e.target.value))}>
                            {playerTeams.map((t) => (
                              <option key={t.id} value={t.id}>
                                {t.name}
                              </option>
                            ))}
                          </select>
                        )}
                      </div>
                    )}
                    <div>
                      <label>Game Type:</label>
                      <div className="game-type-toggle">
                        <button
                          type="button"
                          className={`toggle-option ${gameType === 'League' ? 'active' : ''}`}
                          onClick={() => setGameType('League')}
                        >
                          League
                        </button>
                        <button
                          type="button"
                          className={`toggle-option ${gameType === 'Cup' ? 'active' : ''}`}
                          onClick={() => setGameType('Cup')}
                        >
                          Cup
                        </button>
                      </div>
                    </div>
                    <div>
                      <label>Opponent:</label>
                      <input
                        type="text"
                        value={opponent}
                        onChange={(e) => setOpponent(e.target.value)}
                        placeholder="Enter opponent team name"
                      />
                    </div>
                    <div>
                      <label>Location:</label>
                      <input type="text" value={location} onChange={(e) => setLocation(e.target.value)} placeholder="e.g. Home gym" />
                    </div>
                    <div>
                      <label>Date:</label>
                      <input type="date" value={gameDate} onChange={(e) => setGameDate(e.target.value)} />
                    </div>
                  </div>
                  <button className="start-game-btn" onClick={startGame} disabled={starting}>
                    {starting ? (
                      'Starting...'
                    ) : (
                      <>
                        <svg className="icon" style={{ stroke: '#2b1400' }}><use href="#i-ball" /></svg> Start Game
                      </>
                    )}
                  </button>
                </div>
              )}
            </div>
          ) : (
            <>
              <div className="live-header-actions">
                {!active.shareUrl ? (
                  <button className="share-live-btn" onClick={createShareLink} disabled={sharing}>
                    <svg className="icon"><use href="#i-share" /></svg> {sharing ? 'Creating link...' : 'Share Live'}
                  </button>
                ) : (
                  <button className="share-live-btn" onClick={copyShareLink}>
                    <svg className="icon"><use href="#i-copy" /></svg> {copyMessage ?? 'Copy Share Link'}
                  </button>
                )}
                <button className="end-game-btn" onClick={() => setEnding(true)}>
                  <svg className="icon"><use href="#i-x" /></svg> End Game
                </button>
              </div>

              {ending && (
                <div className="end-game-panel">
                  <h3>Final Score</h3>
                  <div className="form-row">
                    <label>
                      {active.teamName || 'Your Team'}
                      <input type="number" value={finalTeamScore} onChange={(e) => setFinalTeamScore(e.target.value)} />
                    </label>
                    <label>
                      {active.opponent}
                      <input type="number" value={finalOpponentScore} onChange={(e) => setFinalOpponentScore(e.target.value)} />
                    </label>
                  </div>
                  <div className="flex gap-1">
                    <button className="submit-btn" onClick={confirmEndGame}>
                      Confirm &amp; Save Game
                    </button>
                    <button className="nav-btn" onClick={() => setEnding(false)}>
                      Cancel
                    </button>
                  </div>
                </div>
              )}

              <div className="quarter-selector">
                {[1, 2, 3, 4].map((q) => (
                  <button
                    key={q}
                    className={`quarter-btn ${active.currentQuarter === q ? 'active' : ''}`}
                    onClick={() => setActive({ ...active, currentQuarter: q })}
                  >
                    Q{q}
                  </button>
                ))}
                <button className="undo-last-btn" onClick={undoLastAction} disabled={active.actionLog.length === 0}>
                  <svg className="icon"><use href="#i-undo" /></svg> Undo
                </button>
              </div>

              {/* Mobile only: live totals + last few taps, right up top so
                  nothing requires scrolling past the whole button grid. */}
              <div className="live-recent-strip">
                <div className="live-recent-totals">
                  <div className="t tabular"><b>{totalPoints}</b><span>Pts</span></div>
                  <div className="t tabular"><b>{totalRebounds}</b><span>Reb</span></div>
                  <div className="t tabular"><b>{getTotal(['AST'])}</b><span>Ast</span></div>
                  <div className="t tabular"><b>{getTotal(['STL'])}</b><span>Stl</span></div>
                  <div className="t tabular"><b>{getTotal(['BLK'])}</b><span>Blk</span></div>
                  <div className="t tabular"><b>{getTotal(['TO'])}</b><span>To</span></div>
                  <div className="t tabular"><b>{getTotal(['FOUL'])}</b><span>Pf</span></div>
                </div>
                {recentActions.length > 0 && (
                  <div className="live-recent-events">
                    {recentActions.map((a) => (
                      <div key={a.key} className={`recent-chip ${a.positive ? 'positive' : ''}`}>
                        <svg className="icon"><use href={`#${a.icon}`} /></svg>
                        {a.label}
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <div className="live-game-main">
                <div className="live-game-left">
                  <div className="stats-section court-section">
                    <h3><svg className="icon"><use href="#i-target" /></svg> Shot Chart — tap the court</h3>
                    <CourtShotChart shots={chartShots} interactive pendingShot={pendingShot} onCourtTap={handleCourtTap} onRemoveShot={removeShot} />
                    {pendingShot && (
                      <div className="shot-confirm-panel">
                        <span className="shot-confirm-label">{pendingShot.value}PT shot</span>
                        <button className="shot-confirm-btn make" onClick={() => confirmShot(true)} disabled={loggingShot}>
                          <svg className="icon" style={{ stroke: '#10230a' }}><use href="#i-check" /></svg> MAKE
                        </button>
                        <button className="shot-confirm-btn miss" onClick={() => confirmShot(false)} disabled={loggingShot}>
                          <svg className="icon"><use href="#i-x" /></svg> MISS
                        </button>
                        <button className="shot-confirm-cancel" onClick={() => setPendingShot(null)}>
                          Cancel
                        </button>
                      </div>
                    )}
                  </div>

                  <div className="stats-section other-stats">
                    <h3><svg className="icon"><use href="#i-reb" /></svg> Other Stats</h3>
                    <div className="quick-buttons dense">
                      <button className="quick-btn make-ft" onClick={() => addEvent('FT_MAKE')}>
                        <svg className="icon" style={{ stroke: '#10230a' }}><use href="#i-check" /></svg>
                        <span className="qb-label">FT Make</span>
                        <span className="qb-count">{eventStats.counts.FT_MAKE}</span>
                      </button>
                      <button className="quick-btn miss-ft" onClick={() => addEvent('FT_MISS')}>
                        <svg className="icon"><use href="#i-x" /></svg>
                        <span className="qb-label">FT Miss</span>
                        <span className="qb-count">{eventStats.counts.FT_MISS}</span>
                      </button>
                      <button className="quick-btn secondary" onClick={() => addEvent('OREB')}>
                        <svg className="icon"><use href="#i-reb" /></svg>
                        <span className="qb-label">Off. Reb</span>
                        <span className="qb-count">{eventStats.counts.OREB}</span>
                      </button>
                      <button className="quick-btn secondary" onClick={() => addEvent('DREB')}>
                        <svg className="icon"><use href="#i-reb" /></svg>
                        <span className="qb-label">Def. Reb</span>
                        <span className="qb-count">{eventStats.counts.DREB}</span>
                      </button>
                      <button className="quick-btn secondary" onClick={() => addEvent('AST')}>
                        <svg className="icon"><use href="#i-ast" /></svg>
                        <span className="qb-label">Assist</span>
                        <span className="qb-count">{eventStats.counts.AST}</span>
                      </button>
                      <button className="quick-btn secondary" onClick={() => addEvent('STL')}>
                        <svg className="icon"><use href="#i-stl" /></svg>
                        <span className="qb-label">Steal</span>
                        <span className="qb-count">{eventStats.counts.STL}</span>
                      </button>
                      <button className="quick-btn secondary" onClick={() => addEvent('BLK')}>
                        <svg className="icon"><use href="#i-blk" /></svg>
                        <span className="qb-label">Block</span>
                        <span className="qb-count">{eventStats.counts.BLK}</span>
                      </button>
                      <button className="quick-btn secondary" onClick={() => addEvent('TO')}>
                        <svg className="icon"><use href="#i-to" /></svg>
                        <span className="qb-label">Turnover</span>
                        <span className="qb-count">{eventStats.counts.TO}</span>
                      </button>
                      <button className="quick-btn secondary warn" onClick={() => addEvent('FOUL')}>
                        <svg className="icon"><use href="#i-foul" /></svg>
                        <span className="qb-label">Foul</span>
                        <span className="qb-count">{eventStats.counts.FOUL}</span>
                      </button>
                    </div>
                  </div>
                </div>

                <div className="live-game-right">
                  <div className="event-log">
                    <h3>
                      <svg className="icon"><use href="#i-chart" /></svg> Q{active.currentQuarter} Events (
                      {getQuarterEvents(active.currentQuarter).length + getQuarterShots(active.currentQuarter).length})
                    </h3>
                    <div className="events-list">
                      {getQuarterEvents(active.currentQuarter).length === 0 &&
                      getQuarterShots(active.currentQuarter).length === 0 ? (
                        <div className="no-events">No events in Q{active.currentQuarter}</div>
                      ) : (
                        <>
                          {getQuarterShots(active.currentQuarter).map((shot) => (
                              <div key={`shot-${shot.id}`} className={`event-item event-${shot.made ? 'make' : 'miss'}`}>
                                <div className="event-content">
                                  <svg className="icon"><use href={shot.made ? '#i-check' : '#i-x'} /></svg>
                                  <span className="event-display">
                                    {shot.value}PT {shot.made ? 'Make' : 'Miss'}
                                  </span>
                                </div>
                                <button className="delete-event-btn" onClick={() => removeShot(shot.id)} title="Delete this shot">
                                  <svg className="icon" style={{ width: 14, height: 14 }}><use href="#i-x" /></svg>
                                </button>
                              </div>
                            ))}
                          {getQuarterEvents(active.currentQuarter).map((event, idx) => (
                            <div key={event.id} className={`event-item event-${event.type.toLowerCase()}`}>
                              <div className="event-content">
                                <span className="event-number">{idx + 1}.</span>
                                <svg className="icon"><use href={`#${EVENT_ICONS[event.type]}`} /></svg>
                                <span className="event-display">{event.display}</span>
                              </div>
                              <button className="delete-event-btn" onClick={() => removeEvent(event.id)} title="Delete this event">
                                <svg className="icon" style={{ width: 14, height: 14 }}><use href="#i-x" /></svg>
                              </button>
                            </div>
                          ))}
                        </>
                      )}
                    </div>
                    <div className="all-quarters-summary">
                      <h4><svg className="icon"><use href="#i-chart" /></svg> Quarter Summary</h4>
                      {[1, 2, 3, 4].map((q) => {
                        const qEvents = getQuarterEvents(q)
                        const qShots = getQuarterShots(q)
                        const qShotPoints = qShots.filter((s) => s.made).reduce((sum, s) => sum + s.value, 0)
                        const qPoints = qShotPoints + qEvents.filter((e) => e.type === 'FT_MAKE').length
                        const qShotCount = qShots.length
                        return (
                          <div key={q} className="quarter-summary-row">
                            <span className="q-label">Q{q}:</span>
                            <span className="q-events">{qEvents.length + qShotCount} events</span>
                            <span className="q-points">{qPoints} pts</span>
                          </div>
                        )
                      })}
                    </div>
                  </div>
                </div>
              </div>
            </>
          )}
        </div>

        {active && (
          <div className="live-bottom-bar">
            <div className="live-bottom-pts">
              <span className="v">{totalPoints}</span>
              <span className="l">PTS</span>
            </div>
            <div className="live-bottom-chip"><span>{totalRebounds}</span>Reb</div>
            <div className="live-bottom-chip"><span>{getTotal(['AST'])}</span>Ast</div>
            <div className="live-bottom-chip"><span>{getTotal(['STL'])}</span>Stl</div>
            <div className="live-bottom-chip"><span>{getTotal(['BLK'])}</span>Blk</div>
            <div className="live-bottom-chip"><span>{getTotal(['TO'])}</span>To</div>
            <div className="live-bottom-chip"><span>{getTotal(['FOUL'])}</span>Foul</div>
            <div className="live-bottom-chip edit">
              <input
                className="minutes-input"
                type="number"
                min={0}
                max={48}
                value={active.minutesPlayed}
                onChange={(e) => updateMinutesPlayed(Number(e.target.value))}
                aria-label="Minutes played"
              />
              <span>Min</span>
            </div>
            <div className="live-bottom-splits">
              2P {shotsMade(2)}/{shotsAttempted(2)} · 3P {shotsMade(3)}/{shotsAttempted(3)} · FT {getTotal(['FT_MAKE'])}/{getTotal(['FT_MAKE', 'FT_MISS'])}
            </div>
          </div>
        )}
      </div>
    </>
  )
}
