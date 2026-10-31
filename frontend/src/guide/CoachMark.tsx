import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'

interface Box { top: number; left: number; width: number; height: number }

const PAD = 6
const sameBox = (a: Box | null, b: Box | null) =>
  !!a && !!b && Math.abs(a.top - b.top) < 1 && Math.abs(a.left - b.left) < 1 && Math.abs(a.width - b.width) < 1 && Math.abs(a.height - b.height) < 1

// Where an element marked data-guide="<target>" is on screen right now -
// followed as the page scrolls, resizes or re-renders; null while it isn't
// there. "last" picks the last match (a player just added is the last card).
// eslint-disable-next-line react-refresh/only-export-components
export function useGuideTarget(target: string | null, last = false): Box | null {
  const [box, setBox] = useState<Box | null>(null)
  const scrolled = useRef<string | null>(null)

  useEffect(() => {
    if (!target) { setBox(null); return }
    const measure = () => {
      const all = document.querySelectorAll<HTMLElement>(`[data-guide="${target}"]`)
      const el = last ? all[all.length - 1] : all[0]
      const r = el?.getBoundingClientRect()
      const next = r && r.width > 0 ? { top: r.top, left: r.left, width: r.width, height: r.height } : null
      setBox((prev) => (sameBox(prev, next) ? prev : next))
      // Brought into view once, when it first shows up.
      if (el && next && scrolled.current !== target) {
        scrolled.current = target
        if (next.top < 70 || next.top + next.height > window.innerHeight - 160) el.scrollIntoView({ block: 'center', behavior: 'smooth' })
      }
    }
    measure()
    const timer = window.setInterval(measure, 250)
    window.addEventListener('scroll', measure, true)
    window.addEventListener('resize', measure)
    return () => {
      window.clearInterval(timer)
      window.removeEventListener('scroll', measure, true)
      window.removeEventListener('resize', measure)
    }
  }, [target, last])

  return box
}

// A spotlight on a real button and a small card beside it: the page around
// it dims, but everything stays usable - the parent taps the button itself.
export default function CoachMark({
  box,
  title,
  body,
  counter,
  action,
  onSkip,
  skipLabel = 'Skip guide',
}: {
  box: Box
  title: string
  body: string
  counter?: string
  // A "Got it"-style button for a tip; none when the parent should tap the highlighted button.
  action?: { label: string; onClick: () => void }
  onSkip: () => void
  skipLabel?: string
}) {
  const tipRef = useRef<HTMLDivElement>(null)
  const [tipHeight, setTipHeight] = useState(150)
  useLayoutEffect(() => {
    if (tipRef.current) setTipHeight(tipRef.current.offsetHeight)
  }, [title, body])

  const width = Math.min(320, window.innerWidth - 32)
  const below = box.top + box.height + PAD + 12
  const top = below + tipHeight < window.innerHeight - 8 ? below : Math.max(8, box.top - PAD - 12 - tipHeight)
  const left = Math.min(Math.max(16, box.left + box.width / 2 - width / 2), window.innerWidth - width - 16)

  return createPortal(
    <div className="guide-layer">
      <div
        className="guide-spot"
        style={{ top: box.top - PAD, left: box.left - PAD, width: box.width + PAD * 2, height: box.height + PAD * 2 }}
        aria-hidden="true"
      />
      <div ref={tipRef} className="guide-tip" style={{ top, left, width }} role="dialog" aria-label={title} aria-live="polite">
        <strong>{title}</strong>
        <p>{body}</p>
        <div className="guide-tip-foot">
          <span>{counter}</span>
          <span className="guide-tip-actions">
            <button type="button" className="guide-skip" onClick={onSkip}>{skipLabel}</button>
            {action && <button type="button" className="guide-next" onClick={action.onClick}>{action.label}</button>}
          </span>
        </div>
      </div>
    </div>,
    document.body,
  )
}
