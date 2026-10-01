// On/off court tracking during a live game. Each time the player goes on or
// off, the parent enters the period and the time left on the game clock;
// minutes played are worked out from those, on one continuous game clock, so
// a stint that runs across periods (Q2 3:00 -> Q3 5:00) needs no extra taps.
// Periods 1-4 are the 10-minute quarters; 5, 6, ... are 5-minute overtimes.

export const QUARTER_SECONDS = 10 * 60
export const OVERTIME_SECONDS = 5 * 60
export const QUARTERS = 4
export const GAME_SECONDS = QUARTER_SECONDS * QUARTERS

export interface Substitution {
  id: string
  type: 'IN' | 'OUT'
  quarter: number // period: 1-4 quarters, 5+ overtime
  secondsLeft: number
}

export const periodSeconds = (period: number) => (period <= QUARTERS ? QUARTER_SECONDS : OVERTIME_SECONDS)

export const periodLabel = (period: number) => (period <= QUARTERS ? `Q${period}` : period === QUARTERS + 1 ? 'OT' : `OT${period - QUARTERS}`)

// Seconds of game time gone by when the period starts.
const periodStart = (period: number) =>
  period <= QUARTERS ? (period - 1) * QUARTER_SECONDS : GAME_SECONDS + (period - QUARTERS - 1) * OVERTIME_SECONDS

// Seconds of game time gone by at this period + clock reading.
export const elapsedAt = (period: number, secondsLeft: number) => periodStart(period) + (periodSeconds(period) - secondsLeft)

// Game time at the final buzzer, given the last period that was played.
export const endOfGame = (lastPeriod: number) => periodStart(Math.max(QUARTERS, lastPeriod)) + periodSeconds(Math.max(QUARTERS, lastPeriod))

// What the time field shows as digits are typed: the last two digits are the
// seconds, so "630" reads 6:30 and "1000" reads 10:00 - no colon to type.
export function formatClockInput(raw: string): string {
  const digits = raw.replace(/\D/g, '').slice(0, 4)
  if (digits.length <= 2) return digits
  return `${Number(digits.slice(0, -2))}:${digits.slice(-2)}`
}

// "6:30" -> 390 seconds left; "6" (just minutes) -> 360. Null if it isn't a
// clock reading within the period (10:00 a quarter, 5:00 an overtime).
export function parseClock(text: string, period = 1): number | null {
  const match = text.trim().match(/^(\d{1,2})(?:[:.](\d{1,2}))?$/)
  if (!match) return null
  const minutes = Number(match[1])
  const seconds = match[2] === undefined ? 0 : Number(match[2].padEnd(2, '0'))
  if (seconds > 59) return null
  const total = minutes * 60 + seconds
  return total <= periodSeconds(period) ? total : null
}

export const formatClock = (seconds: number) => `${Math.floor(seconds / 60)}:${String(Math.round(seconds % 60)).padStart(2, '0')}`

export const isOnCourt = (subs: Substitution[]) => subs.length > 0 && subs[subs.length - 1].type === 'IN'

// Seconds on court. A stint still open counts up to `untilElapsed` when given
// (the end of the game, when it's finished) - otherwise only finished stints.
export function courtSeconds(subs: Substitution[], untilElapsed?: number): number {
  let total = 0
  let inAt: number | null = null
  for (const sub of subs) {
    const at = elapsedAt(sub.quarter, sub.secondsLeft)
    if (sub.type === 'IN') inAt = at
    else if (inAt !== null) {
      total += at - inAt
      inAt = null
    }
  }
  if (inAt !== null && untilElapsed !== undefined) total += Math.max(0, untilElapsed - inAt)
  return total
}

export const minutesFromSeconds = (seconds: number) => Math.round(seconds / 60)

// Why this substitution can't be recorded, or null when it can.
export function substitutionProblem(subs: Substitution[], next: Omit<Substitution, 'id'>): string | null {
  const last = subs[subs.length - 1]
  const expected = isOnCourt(subs) ? 'OUT' : 'IN'
  if (next.type !== expected) return expected === 'IN' ? 'The player is already off the court.' : 'The player is already on the court.'
  if (last && elapsedAt(next.quarter, next.secondsLeft) < elapsedAt(last.quarter, last.secondsLeft)) {
    return `That's before the last change (${periodLabel(last.quarter)} ${formatClock(last.secondsLeft)}).`
  }
  return null
}
