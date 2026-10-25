import type { GameDto } from '../api/types'

type GameKind = Pick<GameDto, 'gameType' | 'isFromIbba'>
type GameSide = Pick<GameDto, 'isHomeGame'>

// "League" / "Cup" / "Friendly", with the IBBA mark on games synced from IBBA.
export function GameTypeBadge({ game }: { game: GameKind }) {
  return (
    <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>
      {game.gameType}
      {game.isFromIbba && <img className="type-chip-ibba" src="/icons/ibba-logo.png" alt="" title="Synced from IBBA" />}
    </span>
  )
}

// "🏠 Home" / "✈️ Away" - or just the icon (with the word as a tooltip) in a
// table cell. Nothing when the side isn't known.
export function HomeAway({ game, iconOnly = false }: { game: GameSide; iconOnly?: boolean }) {
  if (game.isHomeGame == null) return null
  const [icon, word] = game.isHomeGame ? ['🏠', 'Home'] : ['✈️', 'Away']
  return iconOnly
    ? <span className="type-cell-icon" title={word}>{icon}</span>
    : <span>{icon} {word}</span>
}
