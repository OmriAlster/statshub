import { useEffect, useRef, useState, type SyntheticEvent } from 'react'
import type { GameStatsDto } from '../api/types'
import { sheetMismatch, type SheetRow } from '../utils/statLines'

// What was recorded next to IBBA's scoresheet, differences marked.
export function SheetDiffTable({ rows }: { rows: SheetRow[] }) {
  return (
    <span className="sheet-diff" role="table">
      <span className="sheet-diff-row head" role="row"><span role="columnheader" /><span role="columnheader">Recorded</span><span role="columnheader">IBBA</span></span>
      {rows.map((r) => (
        <span key={r.label} className={`sheet-diff-row${r.differs ? ' differs' : ''}`} role="row">
          <span role="rowheader">{r.label}</span><span role="cell">{r.recorded}</span><span role="cell">{r.sheet}</span>
        </span>
      ))}
    </span>
  )
}

// A "!" on a game whose recorded numbers don't match IBBA's scoresheet - tap
// it for the differences. Sits inside game links, so it never navigates.
export default function SheetMismatch({ stats }: { stats: GameStatsDto | undefined }) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLSpanElement>(null)
  const rows = sheetMismatch(stats)

  useEffect(() => {
    if (!open) return
    const close = (e: PointerEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('pointerdown', close)
    return () => document.removeEventListener('pointerdown', close)
  }, [open])

  if (!rows) return null
  const stop = (e: SyntheticEvent) => { e.preventDefault(); e.stopPropagation() }
  const toggle = (e: SyntheticEvent) => { stop(e); setOpen((o) => !o) }
  return (
    <span className="sheet-mismatch" ref={ref}>
      <span
        role="button"
        tabIndex={0}
        className="sheet-mismatch-icon"
        aria-label="IBBA's scoresheet doesn't match - show the differences"
        aria-expanded={open}
        title="IBBA's scoresheet doesn't match"
        onClick={toggle}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') toggle(e); if (e.key === 'Escape') setOpen(false) }}
      >!</span>
      {open && (
        <span className="sheet-mismatch-pop" onClick={stop}>
          <b>IBBA's scoresheet doesn't match</b>
          <SheetDiffTable rows={rows} />
        </span>
      )}
    </span>
  )
}
