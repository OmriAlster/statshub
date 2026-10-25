import { useEffect, useState } from 'react'
import { api } from './api/client'

// The external leagues players can be linked to (GET /api/leagues) - their
// names and marks, by the key synced games, teams and links carry. Loaded
// once per visit; IBBA, the first league, stands in until then.

export interface League {
  key: string
  name: string
  logoUrl: string
  playerUrlExample: string
}

const IBBA: League = { key: 'ibba', name: 'IBBA', logoUrl: '/icons/ibba-logo.png', playerUrlExample: 'https://ibasketball.co.il/player/…' }

let cached: League[] | null = null
let loading: Promise<League[]> | null = null

function loadLeagues() {
  loading ??= api
    .get<League[]>('/leagues')
    .then((res) => (cached = res.data.length > 0 ? res.data : [IBBA]))
    .catch(() => (cached = [IBBA]))
  return loading
}

export function useLeagues(): League[] {
  const [leagues, setLeagues] = useState<League[]>(cached ?? [IBBA])
  useEffect(() => {
    if (!cached) loadLeagues().then(setLeagues)
  }, [])
  return leagues
}

// One league by its key - IBBA when none is given (older data).
export function useLeague(key?: string | null): League {
  const leagues = useLeagues()
  const wanted = key || IBBA.key
  return leagues.find((l) => l.key === wanted) ?? (wanted === IBBA.key ? IBBA : { ...IBBA, key: wanted, name: wanted })
}

// "IBBA", or "IBBA or Demo League" - for where any of them could be meant.
export function leagueNames(leagues: League[]): string {
  const names = leagues.map((l) => l.name)
  return names.length <= 1 ? names[0] ?? IBBA.name : `${names.slice(0, -1).join(', ')} or ${names[names.length - 1]}`
}
