import type { ReactNode } from 'react'

// The app's "please wait": a small, quick-bouncing basketball before a label
// - "🏀 Loading…". BouncingBall is the block form for a page or panel that's
// loading (centered, or left-aligned with size "sm" inside a card); Busy is
// the same ball inline, inside a button or a line of text.
export default function BouncingBall({ label = 'Loading…', size = 'md' }: { label?: string; size?: 'sm' | 'md' | 'lg' }) {
  return (
    <div className={`bouncing-ball-block size-${size}`}>
      <Busy>{label}</Busy>
    </div>
  )
}

// "🏀 Saving…" - the bouncing ball before a busy label.
export function Busy({ children }: { children: ReactNode }) {
  return (
    <span className="busy-inline" role="status">
      <span className="bouncing-ball-inline" aria-hidden="true">🏀</span>
      {children}
    </span>
  )
}
