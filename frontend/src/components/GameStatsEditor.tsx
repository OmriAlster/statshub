import { useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../api/client'
import { notifyDataChanged } from '../api/dataSync'
import type { GameStatsDto, ShotDto } from '../api/types'
import {
  computeStatsFromEvents,
  EVENT_ICONS,
  EVENT_LABELS,
  seedEventsFromStats,
  type EventType,
  type GameEvent,
} from '../live/gameEvents'
import CourtShotChart, { type ChartShot } from './CourtShotChart'

interface ActionLogEntry {
  kind: 'event' | 'shot'
  id: string | number
  at: number
}

interface GameStatsEditorProps {
  gameStatsId: number
  initialStats: GameStatsDto
}

// The same tap-the-court-and-tap-a-stat recorder used for a live game,
// reused here to edit a game's box score after the fact - the only real
// difference is there's no "end game" step asking for a final score,
// since a past game's score is edited separately as a game field.
export default function GameStatsEditor({ gameStatsId, initialStats }: GameStatsEditorProps) {
  const [events, setEvents] = useState<GameEvent[]>(() => seedEventsFromStats(initialStats))
  const [shots, setShots] = useState<ShotDto[]>([])
  const [actionLog, setActionLog] = useState<ActionLogEntry[]>([])
  const [minutesPlayed, setMinutesPlayed] = useState(initialStats.minutesPlayed)
  const [currentQuarter, setCurrentQuarter] = useState(1)
  const [pendingShot, setPendingShot] = useState<{ x: number; y: number; value: 2 | 3 } | null>(null)
  const [loggingShot, setLoggingShot] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const saveTimer = useRef<number | null>(null)
  const pendingSave = useRef<ReturnType<typeof computeStatsFromEvents> | null>(null)
  const changed = useRef(false)

  useEffect(() => {
    api.get<ShotDto[]>(`/shots/gamestats/${gameStatsId}`).then(({ data }) => setShots(data)).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gameStatsId])

  // Every tap saves on its own, so these writes skip the app-wide refresh
  // signal (refetching every screen per tap is wasted work - this editor
  // already shows the result). Instead, once the editor closes, announce the
  // change a single time so the games table, season stats, and dashboard
  // behind it catch up - flushing a still-debounced last tap first, so that
  // refresh doesn't read the box score from just before it.
  useEffect(() => {
    return () => {
      const notify = () => notifyDataChanged(['stats', 'games', 'shots'])
      if (saveTimer.current && pendingSave.current) {
        window.clearTimeout(saveTimer.current)
        api.put(`/gamestats/${gameStatsId}`, pendingSave.current, { skipDataSync: true }).catch(() => {}).finally(notify)
      } else if (changed.current) {
        notify()
      }
    }
  }, [gameStatsId])

  const persistStats = (nextEvents: GameEvent[], nextMinutes: number) => {
    changed.current = true
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    pendingSave.current = computeStatsFromEvents(nextEvents, nextMinutes)
    saveTimer.current = window.setTimeout(() => {
      saveTimer.current = null
      const payload = pendingSave.current
      pendingSave.current = null
      api.put(`/gamestats/${gameStatsId}`, payload, { skipDataSync: true }).catch(() => {
        setError('Could not save the last stat - check your connection.')
      })
    }, 500)
  }

  const addEvent = (type: EventType) => {
    const newEvent: GameEvent = { id: `${Date.now()}-${Math.random()}`, type, quarter: currentQuarter, display: EVENT_LABELS[type] }
    const nextEvents = [...events, newEvent]
    setEvents(nextEvents)
    setActionLog((prev) => [...prev, { kind: 'event', id: newEvent.id, at: Date.now() }])
    persistStats(nextEvents, minutesPlayed)
  }

  const removeEvent = (eventId: string) => {
    const nextEvents = events.filter((e) => e.id !== eventId)
    setEvents(nextEvents)
    setActionLog((prev) => prev.filter((a) => !(a.kind === 'event' && a.id === eventId)))
    persistStats(nextEvents, minutesPlayed)
  }

  const updateMinutesPlayed = (minutes: number) => {
    const clamped = Math.max(0, Math.min(48, minutes))
    setMinutesPlayed(clamped)
    persistStats(events, clamped)
  }

  const eventStats = useMemo(() => {
    const counts: Record<EventType, number> = { FT_MAKE: 0, FT_MISS: 0, OREB: 0, DREB: 0, AST: 0, STL: 0, BLK: 0, TO: 0, FOUL: 0 }
    const byQuarter: Record<number, GameEvent[]> = {}
    for (const e of events) {
      counts[e.type]++
      ;(byQuarter[e.quarter] ??= []).push(e)
    }
    return { counts, byQuarter }
  }, [events])

  const shotStats = useMemo(() => {
    const byQuarter: Record<number, ShotDto[]> = {}
    let made2 = 0, att2 = 0, made3 = 0, att3 = 0, points = 0
    for (const s of shots) {
      ;(byQuarter[s.quarter] ??= []).push(s)
      if (s.value === 2) { att2++; if (s.made) made2++ } else { att3++; if (s.made) made3++ }
      if (s.made) points += s.value
    }
    return { byQuarter, made2, att2, made3, att3, points }
  }, [shots])

  const getTotal = (types: EventType[]) => types.reduce((sum, t) => sum + eventStats.counts[t], 0)
  const getQuarterEvents = (quarter: number) => eventStats.byQuarter[quarter] ?? []
  const getQuarterShots = (quarter: number) => shotStats.byQuarter[quarter] ?? []

  const handleCourtTap = (x: number, y: number, value: 2 | 3) => {
    if (loggingShot) return
    setPendingShot({ x, y, value })
  }

  const confirmShot = async (made: boolean) => {
    if (!pendingShot) return
    setLoggingShot(true)
    try {
      const { data } = await api.post<ShotDto>('/shots', {
        gameStatsId,
        quarter: currentQuarter,
        x: pendingShot.x,
        y: pendingShot.y,
        made,
        value: pendingShot.value,
      }, { skipDataSync: true })
      changed.current = true
      setShots((prev) => [...prev, data])
      setActionLog((prev) => [...prev, { kind: 'shot', id: data.id, at: Date.now() }])
      setPendingShot(null)
    } catch {
      setError('Could not log that shot - check your connection.')
    } finally {
      setLoggingShot(false)
    }
  }

  const removeShot = async (id: number) => {
    try {
      await api.delete(`/shots/${id}`, { skipDataSync: true })
      changed.current = true
      setShots((prev) => prev.filter((s) => s.id !== id))
      setActionLog((prev) => prev.filter((a) => !(a.kind === 'shot' && a.id === id)))
    } catch {
      setError('Could not remove that shot.')
    }
  }

  const undoLastAction = () => {
    if (actionLog.length === 0) return
    const last = actionLog[actionLog.length - 1]
    if (last.kind === 'event') removeEvent(last.id as string)
    else removeShot(last.id as number)
  }

  const totalPoints = shotStats.points + eventStats.counts.FT_MAKE
  const totalRebounds = eventStats.counts.OREB + eventStats.counts.DREB
  const chartShots: ChartShot[] = shots.map((s) => ({ id: s.id, x: s.x, y: s.y, made: s.made }))

  return (
    <div className="live-game-embed">
      {error && <p className="error">{error}</p>}

      <div className="quarter-selector">
        {[1, 2, 3, 4].map((q) => (
          <button key={q} className={`quarter-btn ${currentQuarter === q ? 'active' : ''}`} onClick={() => setCurrentQuarter(q)}>
            Q{q}
          </button>
        ))}
        <button className="undo-last-btn" onClick={undoLastAction} disabled={actionLog.length === 0}>
          <svg className="icon"><use href="#i-undo" /></svg> Undo
        </button>
      </div>

      <div className="live-game-main">
        <div className="live-game-left">
          <div className="stats-section court-section">
            <h3><svg className="icon"><use href="#i-target" /></svg> Shot Chart — tap the court</h3>
            <CourtShotChart shots={chartShots} interactive pendingShot={pendingShot} onCourtTap={handleCourtTap} onRemoveShot={removeShot} />
            {pendingShot && (
              <div className="shot-confirm-backdrop" onClick={() => setPendingShot(null)}>
                <div className="shot-confirm-panel" onClick={(e) => e.stopPropagation()}>
                  <span className="shot-confirm-label">{pendingShot.value}PT shot</span>
                  <button className="shot-confirm-btn make" onClick={() => confirmShot(true)} disabled={loggingShot}>
                    <svg className="icon" style={{ stroke: '#10230a' }}><use href="#i-check" /></svg> MAKE
                  </button>
                  <button className="shot-confirm-btn miss" onClick={() => confirmShot(false)} disabled={loggingShot}>
                    <svg className="icon"><use href="#i-x" /></svg> MISS
                  </button>
                  <button className="shot-confirm-cancel" onClick={() => setPendingShot(null)}>Cancel</button>
                </div>
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
              <svg className="icon"><use href="#i-chart" /></svg> Q{currentQuarter} Events (
              {getQuarterEvents(currentQuarter).length + getQuarterShots(currentQuarter).length})
            </h3>
            <div className="events-list">
              {getQuarterEvents(currentQuarter).length === 0 && getQuarterShots(currentQuarter).length === 0 ? (
                <div className="no-events">No events in Q{currentQuarter}</div>
              ) : (
                <>
                  {getQuarterShots(currentQuarter).map((shot) => (
                    <div key={`shot-${shot.id}`} className={`event-item event-${shot.made ? 'make' : 'miss'}`}>
                      <div className="event-content">
                        <svg className="icon"><use href={shot.made ? '#i-check' : '#i-x'} /></svg>
                        <span className="event-display">{shot.value}PT {shot.made ? 'Make' : 'Miss'}</span>
                      </div>
                      <button className="delete-event-btn" onClick={() => removeShot(shot.id)} title="Delete this shot">
                        <svg className="icon" style={{ width: 14, height: 14 }}><use href="#i-x" /></svg>
                      </button>
                    </div>
                  ))}
                  {getQuarterEvents(currentQuarter).map((event, idx) => (
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
                return (
                  <div key={q} className="quarter-summary-row">
                    <span className="q-label">Q{q}:</span>
                    <span className="q-events">{qEvents.length + qShots.length} events</span>
                    <span className="q-points">{qPoints} pts</span>
                  </div>
                )
              })}
            </div>
          </div>
        </div>
      </div>

      <div className="live-bottom-bar embedded">
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
            value={minutesPlayed}
            onChange={(e) => updateMinutesPlayed(Number(e.target.value))}
            aria-label="Minutes played"
          />
          <span>Min</span>
        </div>
        <div className="live-bottom-splits">
          2P {shotStats.made2}/{shotStats.att2} · 3P {shotStats.made3}/{shotStats.att3} · FT {getTotal(['FT_MAKE'])}/{getTotal(['FT_MAKE', 'FT_MISS'])}
        </div>
      </div>
    </div>
  )
}
