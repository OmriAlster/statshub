import type { GameStatsDto } from '../api/types'

export type EventType = 'FT_MAKE' | 'FT_MISS' | 'OREB' | 'DREB' | 'AST' | 'STL' | 'BLK' | 'TO' | 'FOUL'

export interface GameEvent {
  id: string
  type: EventType
  quarter: number
  display: string
}

export const EVENT_LABELS: Record<EventType, string> = {
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

export const EVENT_ICONS: Record<EventType, string> = {
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

export function computeStatsFromEvents(events: GameEvent[], minutesPlayed: number) {
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

let seedCounter = 0
function seedId() {
  seedCounter += 1
  return `seed-${Date.now()}-${seedCounter}`
}

// Only the shot chart records each attempt individually - free throws,
// rebounds, assists, etc. were only ever saved as aggregate counts on
// GameStats. To open an already-recorded game in the same event-based
// editor, synthesize one event per counted stat so the totals line up;
// there's no record of which quarter each originally happened in, so
// they all seed into Q1 and only new edits get accurate quarters.
export function seedEventsFromStats(stats: GameStatsDto): GameEvent[] {
  const events: GameEvent[] = []
  const push = (type: EventType, n: number) => {
    for (let i = 0; i < n; i++) {
      events.push({ id: seedId(), type, quarter: 1, display: EVENT_LABELS[type] })
    }
  }
  push('FT_MAKE', stats.freeThrowsMade)
  push('FT_MISS', Math.max(0, stats.freeThrowsAttempted - stats.freeThrowsMade))
  push('OREB', stats.offensiveRebounds)
  push('DREB', stats.defensiveRebounds)
  push('AST', stats.assists)
  push('STL', stats.steals)
  push('BLK', stats.blocks)
  push('TO', stats.turnovers)
  push('FOUL', stats.fouls)
  return events
}
