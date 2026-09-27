import type { GameDto, GameStatsDto, PlayerDto } from '../api/types'

export function makeStats(overrides: Partial<GameStatsDto> = {}): GameStatsDto {
  return {
    id: 1, gameId: 1, playerId: 1, playerName: 'Kid Test',
    fieldGoalsMade: 4, fieldGoalsAttempted: 9, fieldGoalPercentage: 44.4,
    threePointersMade: 2, threePointersAttempted: 5, threePointPercentage: 40,
    freeThrowsMade: 3, freeThrowsAttempted: 4, freeThrowPercentage: 75,
    offensiveRebounds: 2, defensiveRebounds: 5, totalRebounds: 7,
    assists: 4, steals: 2, blocks: 1, turnovers: 3, fouls: 2,
    minutesPlayed: 24, totalPoints: 17,
    ...overrides,
  } as GameStatsDto
}

export function makeGame(overrides: Partial<GameDto> = {}): GameDto {
  return {
    id: 1, teamId: 10, teamName: 'Hawks', teamLogoUrl: null,
    gameType: 'League', opponentName: 'Eagles', opponentLogoUrl: null,
    gameDate: '2026-10-18T16:30:00Z', location: 'Gym', status: 'Upcoming',
    teamScore: null, opponentScore: null, notes: null,
    isHomeGame: true, isFromIbba: false, canRecordLive: true, playerStats: [],
    ...overrides,
  }
}

export function makePlayer(overrides: Partial<PlayerDto> = {}): PlayerDto {
  return { id: 1, firstName: 'Kid', lastName: 'Test', position: 'PG', teams: [], ...overrides } as PlayerDto
}
