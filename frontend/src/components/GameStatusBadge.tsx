import type { GameDto } from '../api/types'

// Reuses the same pulsing-dot "Live" indicator as the live game FAB
// (.fab-live-dot / .live-title-tag in App.css) so a game's live state reads
// the same way everywhere it's shown, not just inside the live tracker.
export default function GameStatusBadge({ status }: { status: GameDto['status'] }) {
  if (status !== 'In Progress') return <>{status}</>

  return (
    <span className="live-title-tag">
      <span className="fab-live-dot" /> Live
    </span>
  )
}
