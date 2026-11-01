import type { GameStatsDto } from '../api/types'

// A null stat wasn't recorded - a scoresheet has only the made shots and the
// fouls. It's shown as "-", and averages and shooting percentages use only
// the games that have it.

export function show(value: number | null | undefined, digits?: number): string {
  if (value === null || value === undefined) return '-'
  return digits === undefined ? String(value) : value.toFixed(digits)
}

export const showPct = (value: number | null | undefined) => (value === null || value === undefined ? '-' : `${value}%`)

// "3/5", or "3/-" when the attempts weren't recorded.
export const madeOf = (made: number, tried: number | null) => `${made}/${show(tried)}`

// Over the items that have the stat - null when none do.
export function average<T>(items: T[], pick: (item: T) => number | null | undefined): number | null {
  const known = items.map(pick).filter((v): v is number => v !== null && v !== undefined)
  return known.length ? known.reduce((s, v) => s + v, 0) / known.length : null
}

export function total<T>(items: T[], pick: (item: T) => number | null | undefined): number | null {
  const known = items.map(pick).filter((v): v is number => v !== null && v !== undefined)
  return known.length ? known.reduce((s, v) => s + v, 0) : null
}

// Made out of attempts, over the items where the attempts were recorded.
export function shooting<T>(items: T[], made: (item: T) => number, tried: (item: T) => number | null | undefined) {
  const known = items.filter((i) => tried(i) !== null && tried(i) !== undefined)
  if (!known.length) return null
  return { made: known.reduce((s, i) => s + made(i), 0), tried: known.reduce((s, i) => s + tried(i)!, 0) }
}

// 2s and 3s together - only when both attempts were recorded.
export const fieldGoalAttempts = (l: GameStatsDto) =>
  l.fieldGoalsAttempted === null || l.threePointersAttempted === null ? null : l.fieldGoalsAttempted + l.threePointersAttempted
