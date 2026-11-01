import { useEffect, useRef, useState, type SyntheticEvent } from 'react'
import type { GameDto, GameStatsDto } from '../api/types'
import { sheetMismatch, type SheetRow } from '../utils/statLines'
import { Busy } from './BouncingBall'

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

// A "!" on a game IBBA's scoresheet has something to say about: the recorded
// numbers don't match it, or nothing is recorded and the sheet has the
// player's line - with a way to use it. Tap it for the details; it sits
// inside game links, so it never navigates.
export default function SheetMismatch({ game, stats, onUseSheet }: {
  game: GameDto
  stats: GameStatsDto | undefined
  // Brings IBBA's numbers in for a game with none - left out where the viewer can't edit.
  onUseSheet?: (game: GameDto) => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const ref = useRef<HTMLSpanElement>(null)
  const rows = sheetMismatch(stats)
  // The games lists load only the viewed player's lines.
  const missing = !stats && game.status === 'Completed' ? game.scoresheets?.[0] : undefined

  useEffect(() => {
    if (!open) return
    const close = (e: PointerEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('pointerdown', close)
    return () => document.removeEventListener('pointerdown', close)
  }, [open])

  if (!rows && !missing) return null
  const label = rows ? "IBBA's scoresheet doesn't match" : 'No stats recorded - IBBA has them'
  const stop = (e: SyntheticEvent) => { e.preventDefault(); e.stopPropagation() }
  const toggle = (e: SyntheticEvent) => { stop(e); setOpen((o) => !o) }
  const use = async (e: SyntheticEvent) => {
    stop(e)
    if (!onUseSheet) return
    setBusy(true)
    try { await onUseSheet(game); setOpen(false) } finally { setBusy(false) }
  }
  return (
    <span className="sheet-mismatch" ref={ref}>
      <span
        role="button"
        tabIndex={0}
        className="sheet-mismatch-icon"
        aria-label={`${label} - show the details`}
        aria-expanded={open}
        title={label}
        onClick={toggle}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') toggle(e); if (e.key === 'Escape') setOpen(false) }}
      >!</span>
      {open && (
        <span className="sheet-mismatch-pop" onClick={stop}>
          <b>{label}</b>
          {rows && <SheetDiffTable rows={rows} />}
          {missing && (
            <>
              <span className="sheet-pop-line">#{missing.number} · {missing.points} pts · {missing.twoPointers}×2 · {missing.threePointers}×3 · {missing.freeThrows} FT</span>
              <span className="sheet-pop-note">From the game's scoresheet: made shots and fouls only.</span>
              {onUseSheet && (
                <span role="button" tabIndex={0} className="sheet-pop-use" aria-disabled={busy} onClick={use} onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') use(e) }}>
                  {busy ? <Busy>Working…</Busy> : "Use IBBA's stats"}
                </span>
              )}
            </>
          )}
        </span>
      )}
    </span>
  )
}
