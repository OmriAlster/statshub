import { describe, expect, it } from 'vitest'
import { makeStats } from '../test/fixtures'
import { average, fieldGoalAttempts, madeAverage, madeOf, sheetMismatch, shooting, show, showPct } from './statLines'

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

  it('averages made shots only over the games whose attempts were recorded', () => {
    // The sheet's 5 makes with no attempts would make it "3.5 of 2" a game.
    expect(madeAverage([tracked, fromSheet], (l) => l.fieldGoalsMade, (l) => l.fieldGoalsAttempted)).toBe(2)
  })
})

describe("IBBA's scoresheet against what was recorded", () => {
  const sheet = { number: 8, starter: true, points: 11, freeThrows: 5, twoPointers: 3, threePointers: 0, fouls: 1, sheetValid: true }
  const recorded = (o: Parameters<typeof makeStats>[0]) =>
    makeStats({ fieldGoalsMade: 3, threePointersMade: 0, freeThrowsMade: 5, totalPoints: 11, fouls: 1, scoresheet: sheet, ...o })

  it('flags a scoring difference, with every row and the differing ones marked', () => {
    const rows = sheetMismatch(recorded({ freeThrowsMade: 4, totalPoints: 10 }))!
    expect(rows.filter((r) => r.differs).map((r) => r.label)).toEqual(['Points', 'FT made'])
    expect(rows.find((r) => r.label === 'FT made')).toMatchObject({ recorded: 4, sheet: 5 })
  })

  it('flags fouls only where some were recorded', () => {
    expect(sheetMismatch(recorded({ fouls: 0 }))).toBeNull()
    expect(sheetMismatch(recorded({ fouls: 3 }))).not.toBeNull()
  })

  it("doesn't flag a match, or the sheet's own numbers", () => {
    expect(sheetMismatch(recorded({}))).toBeNull()
    expect(sheetMismatch(recorded({ source: 'scoresheet', freeThrowsMade: 0 }))).toBeNull()
  })
})
