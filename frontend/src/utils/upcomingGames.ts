import type { GameDto, PlayerDto } from '../api/types'

// The Dashboard's Upcoming Games list: every player's games that are live or
// still to come, one row per game.
export interface UpcomingGame {
  game: GameDto
  // Every one of your players on this game - two siblings on the same team
  // share one row instead of listing the game twice.
  players: PlayerDto[]
}

export const UPCOMING_PREVIEW_COUNT = 5

// A manual game nobody entered a score for stays "Upcoming" after it's
// played - keep it listed only for a few hours past tip-off, not forever.
const UPCOMING_GRACE_MS = 3 * 60 * 60 * 1000

export function collectUpcomingGames(cards: { player: PlayerDto; gamesByTeam: Record<number, GameDto[]> }[]): UpcomingGame[] {
  const byId = new Map<number, UpcomingGame>()
  const cutoff = Date.now() - UPCOMING_GRACE_MS
  for (const { player, gamesByTeam } of cards) {
    for (const game of Object.values(gamesByTeam).flat()) {
      const isLive = game.status === 'In Progress'
      const isUpcoming = game.status === 'Upcoming' && new Date(game.gameDate).getTime() >= cutoff
      if (!isLive && !isUpcoming) continue
      const entry = byId.get(game.id)
      if (entry) {
        if (!entry.players.some((p) => p.id === player.id)) entry.players.push(player)
      } else {
        byId.set(game.id, { game, players: [player] })
      }
    }
  }
  // Live games first, then soonest first.
  return [...byId.values()].sort((a, b) => {
    const liveA = a.game.status === 'In Progress' ? 0 : 1
    const liveB = b.game.status === 'In Progress' ? 0 : 1
    if (liveA !== liveB) return liveA - liveB
    return new Date(a.game.gameDate).getTime() - new Date(b.game.gameDate).getTime()
  })
}
