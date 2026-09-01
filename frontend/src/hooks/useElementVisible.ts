import { useEffect, useRef, useState } from 'react'

// Tracks whether a DOM node is currently intersecting the viewport - used to
// drive a `position: fixed` bar that only shows while its "real" on-page
// counterpart is scrolled out of view. Plain CSS `position: sticky` can't do
// this here because the games table needs `overflow-x: auto` for mobile,
// and any non-visible overflow on an ancestor makes IT (not the page) the
// sticky containing block, so a sticky row can only ever stick within the
// table's own box, never the actual viewport.
export function useElementVisible<T extends Element>() {
  const ref = useRef<T>(null)
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    const el = ref.current
    if (!el) return
    const observer = new IntersectionObserver(([entry]) => setVisible(entry.isIntersecting), { threshold: 0 })
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  return [ref, visible] as const
}
