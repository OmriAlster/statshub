import { describe, expect, it } from 'vitest'
import { courtSeconds, endOfGame, formatClockInput, GAME_SECONDS, isOnCourt, minutesFromSeconds, parseClock, periodLabel, substitutionProblem, type Substitution } from './courtTime'

let n = 0
const sub = (type: 'IN' | 'OUT', quarter: number, clock: string): Substitution => ({ id: String(++n), type, quarter, secondsLeft: parseClock(clock)! })

describe('court time', () => {
  it('reads the game clock the way it is typed', () => {
    expect(parseClock('6:30')).toBe(390)
    expect(parseClock('6.30')).toBe(390)
    expect(parseClock('6')).toBe(360)
    expect(parseClock('0:05')).toBe(5)
    expect(parseClock('10:00')).toBe(600)
    expect(parseClock('10:01')).toBeNull() // a quarter is 10 minutes
    expect(parseClock('6:75')).toBeNull()
    expect(parseClock('abc')).toBeNull()
  })

  it('counts a stint inside one quarter', () => {
    expect(courtSeconds([sub('IN', 1, '10:00'), sub('OUT', 1, '4:00')])).toBe(360)
  })

  it('counts a stint across quarters with no extra taps', () => {
    // Q2 3:00 left -> Q3 5:00 left = 3 min of Q2 + 5 min of Q3
    expect(courtSeconds([sub('IN', 2, '3:00'), sub('OUT', 3, '5:00')])).toBe(8 * 60)
  })

  it('adds up several stints', () => {
    const subs = [sub('IN', 1, '10:00'), sub('OUT', 1, '2:00'), sub('IN', 3, '10:00'), sub('OUT', 4, '6:30')]
    expect(minutesFromSeconds(courtSeconds(subs))).toBe(8 + 14) // 8:00 + 13:30 -> 21:30 rounds to 22
  })

  it('counts a player still on court to the end of the game once it is over', () => {
    const subs = [sub('IN', 4, '5:00')]
    expect(isOnCourt(subs)).toBe(true)
    expect(courtSeconds(subs)).toBe(0) // not finished yet
    expect(courtSeconds(subs, GAME_SECONDS)).toBe(300)
  })

  it('refuses a change that is out of order', () => {
    const subs = [sub('IN', 2, '5:00')]
    expect(substitutionProblem(subs, { type: 'IN', quarter: 2, secondsLeft: 200 })).toMatch(/already on the court/)
    expect(substitutionProblem(subs, { type: 'OUT', quarter: 2, secondsLeft: 400 })).toMatch(/before the last change/)
    expect(substitutionProblem(subs, { type: 'OUT', quarter: 3, secondsLeft: 600 })).toBeNull()
    expect(substitutionProblem([], { type: 'OUT', quarter: 1, secondsLeft: 600 })).toMatch(/already off the court/)
  })
})

describe('overtime and typing the clock', () => {
  it('counts overtime as 5-minute periods after the 4 quarters', () => {
    // In Q4 2:00 -> out OT1 1:00 = 2:00 of Q4 + 4:00 of OT
    expect(courtSeconds([sub('IN', 4, '2:00'), { id: 'x', type: 'OUT', quarter: 5, secondsLeft: 60 }])).toBe(6 * 60)
    expect(parseClock('5:00', 5)).toBe(300)
    expect(parseClock('6:00', 5)).toBeNull() // an overtime is 5 minutes
    expect(periodLabel(4)).toBe('Q4')
    expect(periodLabel(5)).toBe('OT')
    expect(periodLabel(6)).toBe('OT2')
  })

  it('runs a stint still going to the final buzzer, overtime included', () => {
    expect(endOfGame(4)).toBe(GAME_SECONDS)
    expect(endOfGame(5)).toBe(GAME_SECONDS + 300)
    const subs = [{ id: 'y', type: 'IN' as const, quarter: 5, secondsLeft: 120 }]
    expect(courtSeconds(subs, endOfGame(5))).toBe(120)
  })

  it('puts the colon in as the digits are typed', () => {
    expect(formatClockInput('6')).toBe('6')
    expect(formatClockInput('63')).toBe('63')
    expect(formatClockInput('630')).toBe('6:30')
    expect(formatClockInput('1000')).toBe('10:00')
    expect(formatClockInput('6:3')).toBe('63') // editing an already formatted value
    expect(formatClockInput('045')).toBe('0:45')
  })
})
