import { afterEach, describe, expect, it, vi } from 'vitest'
import { makeGame, makePlayer } from '../test/fixtures'
import { countsTowardStats } from './countedGames'
import { formatGameDateOnly, formatGameDateTimeFull, formatGameTime } from './formatGameDate'
import { collectUpcomingGames } from './upcomingGames'

describe('friendly games', () => {
  it('only friendly games are left out of stats', () => {
    expect(countsTowardStats({ gameType: 'League' })).toBe(true)
    expect(countsTowardStats({ gameType: 'Cup' })).toBe(true)
    expect(countsTowardStats({ gameType: 'Friendly' })).toBe(false)
  })
})

describe('game dates are shown in local time', () => {
  // 16:30 UTC is 19:30 in Israel in October (summer time).
  it('formats date, time and full date', () => {
    expect(formatGameDateOnly('2026-10-18T16:30:00Z')).toBe('October 18, 2026')
    expect(formatGameTime('2026-10-18T16:30:00Z')).toBe('7:30 PM')
    expect(formatGameDateTimeFull('2026-10-18T16:30:00Z')).toContain('Sunday, October 18, 2026')
  })

  it('a late-evening UTC game lands on the next local day', () => {
    expect(formatGameDateOnly('2026-12-31T23:00:00Z')).toBe('January 1, 2027')
  })
})

describe('Dashboard upcoming games', () => {
  afterEach(() => vi.useRealTimers())

  it('lists live games first, then soonest first, and drops played or stale games', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-10-10T12:00:00Z'))
    const games = [
      makeGame({ id: 1, gameDate: '2026-10-20T16:00:00Z' }),
      makeGame({ id: 2, gameDate: '2026-10-12T16:00:00Z' }),
      makeGame({ id: 3, gameDate: '2026-10-10T09:00:00Z' }), // 3h ago, not scored yet: still listed
      makeGame({ id: 4, gameDate: '2026-10-09T09:00:00Z' }), // a day ago, never scored: dropped
      makeGame({ id: 5, gameDate: '2026-10-08T09:00:00Z', status: 'Completed' }),
      makeGame({ id: 6, gameDate: '2026-10-25T16:00:00Z', status: 'In Progress' }),
    ]
    const upcoming = collectUpcomingGames([{ player: makePlayer(), gamesByTeam: { 10: games } }])
    expect(upcoming.map((u) => u.game.id)).toEqual([6, 3, 2, 1])
  })

  it('siblings on the same game share one row', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-10-10T12:00:00Z'))
    const game = makeGame({ id: 7, gameDate: '2026-10-12T16:00:00Z' })
    const upcoming = collectUpcomingGames([
      { player: makePlayer({ id: 1, firstName: 'Older' }), gamesByTeam: { 10: [game] } },
      { player: makePlayer({ id: 2, firstName: 'Younger' }), gamesByTeam: { 11: [{ ...game, teamId: 11 }] } },
    ])
    expect(upcoming).toHaveLength(1)
    expect(upcoming[0].players.map((p) => p.firstName)).toEqual(['Older', 'Younger'])
  })
})
