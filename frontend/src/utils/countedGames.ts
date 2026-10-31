import type { GameDto } from '../api/types'

// Friendly games are tracked like any other (schedule, live scoring, box
// score) but never count toward stats - record, averages, totals, shot
// charts. The backend applies the same rule to season stats and shot charts
// (Game.FriendlyGameType).
export const countsTowardStats = (game: Pick<GameDto, 'gameType'>) => game.gameType !== 'Friendly'

// League and cup games that have a result, oldest first.
export function playedGames(games: GameDto[]) {
  return games
    .filter((g) => g.status === 'Completed' && g.teamScore != null && g.opponentScore != null && countsTowardStats(g))
    .sort((a, b) => new Date(a.gameDate).getTime() - new Date(b.gameDate).getTime())
}
