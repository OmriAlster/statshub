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

// Made shots per game, over the games whose attempts were recorded - a game
// with makes but no attempts would read as "3 of 0".
export const madeAverage = <T>(items: T[], made: (item: T) => number | null | undefined, tried: (item: T) => number | null | undefined) =>
  average(items.filter((i) => tried(i) !== null && tried(i) !== undefined), made)

// Made out of attempts, over the items where the attempts were recorded.
export function shooting<T>(items: T[], made: (item: T) => number, tried: (item: T) => number | null | undefined) {
  const known = items.filter((i) => tried(i) !== null && tried(i) !== undefined)
  if (!known.length) return null
  return { made: known.reduce((s, i) => s + made(i), 0), tried: known.reduce((s, i) => s + tried(i)!, 0) }
}

// 2s and 3s together - only when both attempts were recorded.
export const fieldGoalAttempts = (l: GameStatsDto) =>
  l.fieldGoalsAttempted === null || l.threePointersAttempted === null ? null : l.fieldGoalsAttempted + l.threePointersAttempted

export type SheetStat = 'points' | 'two' | 'three' | 'ft' | 'fouls'
export interface SheetRow { stat: SheetStat; label: string; recorded: number; sheet: number; differs: boolean }

// The recorded numbers next to IBBA's scoresheet, when they don't match: the
// scoring, and the fouls when any were recorded (0 is usually "not tracked").
// Null when they match, or when there's nothing to hold them against - no
// sheet, or the stats are IBBA's own (the sheet's, or a league box score).
export function sheetMismatch(stats: GameStatsDto | undefined): SheetRow[] | null {
  const sheet = stats?.scoresheet
  if (!stats || !sheet || stats.source) return null
  const rows = [
    { stat: 'points' as const, label: 'Points', recorded: stats.totalPoints, sheet: sheet.points, counts: true },
    { stat: 'two' as const, label: '2PT made', recorded: stats.fieldGoalsMade, sheet: sheet.twoPointers, counts: true },
    { stat: 'three' as const, label: '3PT made', recorded: stats.threePointersMade, sheet: sheet.threePointers, counts: true },
    { stat: 'ft' as const, label: 'FT made', recorded: stats.freeThrowsMade, sheet: sheet.freeThrows, counts: true },
    { stat: 'fouls' as const, label: 'Fouls', recorded: stats.fouls, sheet: sheet.fouls, counts: stats.fouls > 0 },
  ]
  if (!rows.some((r) => r.counts && r.recorded !== r.sheet)) return null
  return rows.map(({ stat, label, recorded, sheet: onSheet }) => ({ stat, label, recorded, sheet: onSheet, differs: recorded !== onSheet }))
}

// The class that marks a stat IBBA's scoresheet disagrees with ("" when it doesn't).
export function sheetOff(stats: GameStatsDto | undefined) {
  const differs = new Set((sheetMismatch(stats) ?? []).filter((r) => r.differs).map((r) => r.stat))
  return (stat: SheetStat) => (differs.has(stat) ? 'sheet-off' : '')
}
