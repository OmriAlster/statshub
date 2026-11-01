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

// A stat the saved line didn't have (null - a scoresheet's line has only the
// made shots and fouls) stays unrecorded - left out, so the server keeps it
// null - until something of its kind is tapped in.
export function computeStatsFromEvents(events: GameEvent[], minutesPlayed: number | null, saved?: Partial<GameStatsDto>) {
  const count = (types: EventType[]) => events.filter((e) => types.includes(e.type)).length
  const known = (value: number, types: EventType[], before: number | null | undefined) =>
    before === null && count(types) === 0 ? undefined : value
  return {
    freeThrowsMade: count(['FT_MAKE']),
    freeThrowsAttempted: known(count(['FT_MAKE', 'FT_MISS']), ['FT_MISS'], saved?.freeThrowsAttempted),
    offensiveRebounds: known(count(['OREB']), ['OREB'], saved?.offensiveRebounds),
    defensiveRebounds: known(count(['DREB']), ['DREB'], saved?.defensiveRebounds),
    assists: known(count(['AST']), ['AST'], saved?.assists),
    steals: known(count(['STL']), ['STL'], saved?.steals),
    blocks: known(count(['BLK']), ['BLK'], saved?.blocks),
    turnovers: known(count(['TO']), ['TO'], saved?.turnovers),
    fouls: count(['FOUL']),
    minutesPlayed: minutesPlayed ?? undefined,
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
  const push = (type: EventType, n: number | null) => {
    for (let i = 0; i < (n ?? 0); i++) {
      events.push({ id: seedId(), type, quarter: 1, display: EVENT_LABELS[type] })
    }
  }
  push('FT_MAKE', stats.freeThrowsMade)
  push('FT_MISS', stats.freeThrowsAttempted === null ? 0 : Math.max(0, stats.freeThrowsAttempted - stats.freeThrowsMade))
  push('OREB', stats.offensiveRebounds)
  push('DREB', stats.defensiveRebounds)
  push('AST', stats.assists)
  push('STL', stats.steals)
  push('BLK', stats.blocks)
  push('TO', stats.turnovers)
  push('FOUL', stats.fouls)
  return events
}
