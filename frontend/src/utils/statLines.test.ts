import { describe, expect, it } from 'vitest'
import { makeStats } from '../test/fixtures'
import { average, fieldGoalAttempts, madeOf, shooting, show, showPct } from './statLines'

describe('stats a game may not have recorded', () => {
  const tracked = makeStats({ fieldGoalsMade: 2, fieldGoalsAttempted: 4, threePointersMade: 1, threePointersAttempted: 2, defensiveRebounds: 6, offensiveRebounds: 0 })
  const fromSheet = makeStats({ fieldGoalsMade: 5, fieldGoalsAttempted: null, threePointersMade: 1, threePointersAttempted: null, totalRebounds: null })

  it('shows an unrecorded stat as "-"', () => {
    expect(show(null)).toBe('-')
    expect(show(2.345, 1)).toBe('2.3')
    expect(showPct(null)).toBe('-')
    expect(madeOf(5, null)).toBe('5/-')
    expect(madeOf(2, 4)).toBe('2/4')
  })

  it('averages over the games that recorded it', () => {
    expect(average([tracked, fromSheet], (l) => l.totalRebounds)).toBe(tracked.totalRebounds)
    expect(average([fromSheet], (l) => l.totalRebounds)).toBeNull()
  })

  it("leaves a game without attempts out of the shooting - its makes too", () => {
    expect(shooting([tracked, fromSheet], (l) => l.fieldGoalsMade + l.threePointersMade, fieldGoalAttempts)).toEqual({ made: 3, tried: 6 })
    expect(shooting([fromSheet], (l) => l.fieldGoalsMade, (l) => l.fieldGoalsAttempted)).toBeNull()
  })
})
