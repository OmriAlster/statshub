import { useEffect, useRef } from 'react'

// App-wide "something changed, refetch what you show" signal. Every write
// request (see the response interceptor in client.ts) announces which kinds
// of data it touched; every screen that displays that kind of data refetches
// silently. This is what keeps screens other than the one you acted on in
// sync - e.g. scheduling a game on the Stats page shows up in the live
// widget's "Today's Games", and linking an IBBA team shows the team on the
// player's profile - without each action having to know every place its
// result is displayed.
export type DataTopic = 'players' | 'teams' | 'games' | 'stats' | 'shots' | 'ibba' | 'seasons'

export const ALL_TOPICS: DataTopic[] = ['players', 'teams', 'games', 'stats', 'shots', 'ibba', 'seasons']

type Listener = (topics: ReadonlySet<DataTopic>) => void
const listeners = new Set<Listener>()

export function notifyDataChanged(topics: DataTopic[]) {
  if (topics.length === 0) return
  const set = new Set(topics)
  listeners.forEach((listener) => listener(set))
}

// Which kinds of data a write to this URL can change. Deliberately broad
// where the backend's side effects are broad: an IBBA sync creates teams,
// games, logos and the player's photo; a roster change changes which games
// a player sees; a shot also updates the box score it belongs to.
export function topicsForWrite(url: string): DataTopic[] {
  const path = url.split('?')[0].replace(/^https?:\/\/[^/]+/, '').replace(/^\/api/, '')

  if (/^\/(auth|push|share)(\/|$)/.test(path)) return []
  if (/^\/players\/\d+\/(invite|parent-invite)$/.test(path)) return []
  if (path.startsWith('/ibba') || /^\/players\/\d+\/ibba/.test(path)) return ALL_TOPICS
  if (path.startsWith('/players') || path.startsWith('/teams') || path.startsWith('/seasons')) return ALL_TOPICS
  if (path.startsWith('/gamestats')) return ['stats', 'games']
  if (path.startsWith('/shots')) return ['shots', 'stats', 'games']
  if (path.startsWith('/games')) return ['games', 'stats']
  return ALL_TOPICS
}

// IBBA writes kick off background work on the server that finishes after
// the response (team logos, cup/friendly opponents resolved from their own
// pages) - refetch again a little later so those show up on their own.
export function isIbbaWrite(url: string) {
  const path = url.split('?')[0]
  return path.includes('/ibba') && !path.includes('/ibba/preview')
}

export function scheduleIbbaFollowUps() {
  for (const delay of [6000, 15000]) {
    window.setTimeout(() => notifyDataChanged(ALL_TOPICS), delay)
  }
}

// Coming back to the app (phone unlock, switching back to the tab) after a
// while - a co-parent may have changed things, or an IBBA sync may have run.
const RESUME_REFRESH_AFTER_MS = 15000
if (typeof document !== 'undefined') {
  let hiddenAt: number | null = null
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') {
      hiddenAt = Date.now()
    } else if (hiddenAt !== null && Date.now() - hiddenAt >= RESUME_REFRESH_AFTER_MS) {
      hiddenAt = null
      notifyDataChanged(ALL_TOPICS)
    }
  })
}

// Calls refresh (always the latest version passed in, so it sees current
// state) whenever any of the given topics change. Several writes in a row
// (e.g. create team -> add player -> link IBBA team) coalesce into a single
// refresh.
export function useDataRefresh(topics: DataTopic[], refresh: () => void, enabled = true) {
  const refreshRef = useRef(refresh)
  useEffect(() => {
    refreshRef.current = refresh
  })

  const topicsKey = topics.join(',')

  useEffect(() => {
    if (!enabled) return
    const wanted = new Set(topicsKey.split(',') as DataTopic[])
    let timer: number | null = null

    const listener: Listener = (changed) => {
      if (![...changed].some((t) => wanted.has(t))) return
      if (timer !== null) window.clearTimeout(timer)
      timer = window.setTimeout(() => {
        timer = null
        refreshRef.current()
      }, 250)
    }

    listeners.add(listener)
    return () => {
      listeners.delete(listener)
      if (timer !== null) window.clearTimeout(timer)
    }
  }, [topicsKey, enabled])
}
