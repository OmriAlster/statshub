import { describe, expect, it } from 'vitest'
import { makeStats } from '../test/fixtures'
import { computeStatsFromEvents, EVENT_LABELS, seedEventsFromStats, type EventType, type GameEvent } from './gameEvents'

const event = (type: EventType, quarter = 1): GameEvent => ({ id: `${type}-${Math.random()}`, type, quarter, display: EVENT_LABELS[type] })

describe('live-game events -> box score', () => {
  it('counts each kind of event, with free throw attempts including misses', () => {
    const stats = computeStatsFromEvents(
      [event('FT_MAKE'), event('FT_MAKE'), event('FT_MISS'), event('OREB'), event('DREB', 2), event('DREB', 3), event('AST'), event('STL'), event('BLK'), event('TO'), event('FOUL'), event('FOUL')],
      18,
    )
    expect(stats).toEqual({
      freeThrowsMade: 2, freeThrowsAttempted: 3,
      offensiveRebounds: 1, defensiveRebounds: 2,
      assists: 1, steals: 1, blocks: 1, turnovers: 1, fouls: 2,
      minutesPlayed: 18,
    })
  })

  it('never sends shot totals - the server keeps those from the shot chart', () => {
    const stats = computeStatsFromEvents([event('FT_MAKE')], 0)
    expect(stats).not.toHaveProperty('fieldGoalsMade')
    expect(stats).not.toHaveProperty('threePointersMade')
  })

  it('reopening a saved game rebuilds events that add back up to the same box score', () => {
    const saved = makeStats({ freeThrowsMade: 3, freeThrowsAttempted: 5, offensiveRebounds: 1, defensiveRebounds: 4, assists: 2, steals: 0, blocks: 1, turnovers: 2, fouls: 3, minutesPlayed: 20 })
    const events = seedEventsFromStats(saved)
    const rebuilt = computeStatsFromEvents(events, saved.minutesPlayed)

    expect(rebuilt.freeThrowsMade).toBe(3)
    expect(rebuilt.freeThrowsAttempted).toBe(5)
    expect(rebuilt.defensiveRebounds).toBe(4)
    expect(rebuilt.fouls).toBe(3)
    expect(new Set(events.map((e) => e.id)).size).toBe(events.length) // undo/delete rely on unique ids
  })

  it("a scoresheet's unrecorded stats stay unrecorded until something of their kind is tapped", () => {
    const sheet = makeStats({
      source: 'scoresheet', freeThrowsMade: 2, freeThrowsAttempted: null, offensiveRebounds: null, defensiveRebounds: null,
      assists: null, steals: null, blocks: null, turnovers: null, fouls: 1, minutesPlayed: null,
    })
    const events = [...seedEventsFromStats(sheet), event('AST')]
    const stats = computeStatsFromEvents(events, sheet.minutesPlayed, sheet)

    expect(stats.freeThrowsMade).toBe(2)
    expect(stats.assists).toBe(1) // tapped - recorded now
    for (const key of ['freeThrowsAttempted', 'offensiveRebounds', 'steals', 'minutesPlayed'] as const) expect(stats[key]).toBeUndefined()
    // A missed free throw makes the attempts known.
    expect(computeStatsFromEvents([...events, event('FT_MISS')], null, sheet).freeThrowsAttempted).toBe(3)
  })

  it('more made than attempted free throws does not create negative misses', () => {
    const events = seedEventsFromStats(makeStats({ freeThrowsMade: 4, freeThrowsAttempted: 2 }))
    expect(events.filter((e) => e.type === 'FT_MISS')).toHaveLength(0)
  })
})
