import type { GameDto } from '../api/types'
import { LeagueMark } from './LeagueBadge'

type GameKind = Pick<GameDto, 'gameType' | 'isFromIbba' | 'league'>
type GameSide = Pick<GameDto, 'isHomeGame'>

// "League" / "Cup" / "Friendly", with the league's mark on games synced from one.
export function GameTypeBadge({ game }: { game: GameKind }) {
  return (
    <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>
      {game.gameType}
      {game.isFromIbba && <LeagueMark league={game.league} className="type-chip-ibba" />}
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
