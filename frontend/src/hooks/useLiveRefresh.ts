import { useEffect, useRef } from 'react'
import { useLiveGames } from '../live/liveUpdates'

// Keeps what's shown (score, box score, on court / on bench) current with
// whoever is tracking a game on another phone.
//
// With `players` given, a change to any of their games reloads the page the
// moment it's saved (the live connection - see live/liveUpdates) - including
// a game that only just started. While that connection is down, and only
// while a game on the page is live, it reloads every few seconds instead.
export function useLiveRefresh(isLive: boolean, reload: () => void, everyMs = 10000, players: string[] = []) {
  const latest = useRef(reload)
  latest.current = reload

  // A tracker's tap can save more than one thing (a box score and a shot) -
  // one reload for them all.
  const pending = useRef<number | undefined>(undefined)
  const connected = useLiveGames(players.map((player) => ({ player })), () => {
    window.clearTimeout(pending.current)
    pending.current = window.setTimeout(() => latest.current(), 250)
  })
  useEffect(() => () => window.clearTimeout(pending.current), [])

  useEffect(() => {
    if (!isLive || (connected && players.length > 0)) return
    const interval = window.setInterval(() => {
      if (document.visibilityState === 'visible') latest.current()
    }, everyMs)
    return () => window.clearInterval(interval)
  }, [isLive, everyMs, connected, players.length])
}
