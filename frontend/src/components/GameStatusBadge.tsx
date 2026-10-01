import type { GameDto } from '../api/types'

// Reuses the same pulsing-dot "Live" indicator as the live game FAB
// (.fab-live-dot / .live-title-tag in App.css) so a game's live state reads
// the same way everywhere it's shown, not just inside the live tracker.
// While live, also shows whether the player is on the court or on the bench,
// when whoever tracks the game uses Sub in / Sub out.
export default function GameStatusBadge({ status, onCourt }: { status: GameDto['status']; onCourt?: boolean | null }) {
  if (status !== 'In Progress') return <>{status}</>

  return (
    <span className="live-badge-group">
      <span className="live-title-tag">
        <span className="fab-live-dot" /> Live
      </span>
      {onCourt != null && <OnCourtChip onCourt={onCourt} />}
    </span>
  )
}

export function OnCourtChip({ onCourt }: { onCourt: boolean }) {
  return (
    <span className={`on-court-chip ${onCourt ? 'on' : 'off'}`}>
      <span className="on-court-chip-dot" aria-hidden="true" />
      {onCourt ? 'On court' : 'Bench'}
    </span>
  )
}

// The player's on/off court in a game (the given player's, or the one player
// whose stats the game carries) - null when nobody tracks it.
export function onCourtIn(game: GameDto, playerId?: number): boolean | null {
  const stats = playerId != null ? game.playerStats.find((s) => s.playerId === playerId) : game.playerStats.find((s) => s.onCourt != null)
  return stats?.onCourt ?? null
}
