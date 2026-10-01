import { useEffect, useRef } from 'react'

// While a game on the page is live, reload it every few seconds so what's
// shown (score, box score, on court / on bench) keeps up with whoever is
// tracking it on another phone. Stops as soon as nothing is live.
export function useLiveRefresh(isLive: boolean, reload: () => void, everyMs = 10000) {
  const latest = useRef(reload)
  latest.current = reload
  useEffect(() => {
    if (!isLive) return
    const interval = window.setInterval(() => {
      if (document.visibilityState === 'visible') latest.current()
    }, everyMs)
    return () => window.clearInterval(interval)
  }, [isLive, everyMs])
}
