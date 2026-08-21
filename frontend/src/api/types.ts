export interface TeamDto {
  id: number
  name: string
  // Only present when this team appears in a player-specific context (e.g.
  // PlayerDto.teams) - a player can wear a different number per team.
  jerseyNumber?: number | null
}

export interface ParentDto {
  userId: number
  firstName: string
  lastName: string
}

export interface PlayerDto {
  id: number
  firstName: string
  lastName: string
  jerseyNumber: number
  position: string
  height?: number | null
  weight?: number | null
  dateOfBirth: string
  profilePictureUrl?: string | null
  teams: TeamDto[]
  parents: ParentDto[]
}

export interface UserDto {
  id: number
  email: string
  firstName: string
  lastName: string
  profilePictureUrl?: string | null
  role: 'Parent' | 'Player'
  linkedPlayer?: PlayerDto | null
}

export interface SeasonDto {
  id: number
  name: string
  sport: string
  year: number
  startDate: string
  endDate?: string | null
  totalGames: number
}

export interface GameStatsDto {
  id: number
  gameId: number
  playerId: number
  playerName: string
  fieldGoalsMade: number
  fieldGoalsAttempted: number
  fieldGoalPercentage: number
  threePointersMade: number
  threePointersAttempted: number
  threePointPercentage: number
  freeThrowsMade: number
  freeThrowsAttempted: number
  freeThrowPercentage: number
  offensiveRebounds: number
  defensiveRebounds: number
  totalRebounds: number
  assists: number
  steals: number
  blocks: number
  turnovers: number
  fouls: number
  minutesPlayed: number
  totalPoints: number
}

export type GameType = 'League' | 'Cup'

export interface GameDto {
  id: number
  teamId: number
  teamName: string
  gameType: GameType
  opponentName: string
  gameDate: string
  location: string
  status: 'Upcoming' | 'In Progress' | 'Completed'
  teamScore?: number | null
  opponentScore?: number | null
  notes?: string | null
  playerStats: GameStatsDto[]
}

export interface UpdateGameDto {
  opponentName?: string
  gameDate?: string
  location?: string
  status?: GameDto['status']
  gameType?: GameType
  teamScore?: number | null
  opponentScore?: number | null
  notes?: string
}

export interface PlayerTeamStatsDto {
  playerId: number
  playerName: string
  jerseyNumber: number
  position: string
  teamId: number
  teamName: string
  gamesPlayed: number
  totalMinutes: number
  totalPoints: number
  pointsPerGame: number
  totalRebounds: number
  reboundsPerGame: number
  totalAssists: number
  assistsPerGame: number
  totalSteals: number
  stealsPerGame: number
  totalBlocks: number
  blocksPerGame: number
  totalTurnovers: number
  turnoversPerGame: number
  fieldGoalPercentage: number
  threePointPercentage: number
  freeThrowPercentage: number
}

export interface SharedPlayerDto {
  playerName: string
  jerseyNumber: number
  position: string
  profilePictureUrl?: string | null
  game?: GameDto | null
  teams: PlayerTeamStatsDto[]
  recentGames: GameDto[]
}

export interface ShotDto {
  id: number
  gameStatsId: number
  gameId: number
  playerId: number
  quarter: number
  x: number
  y: number
  made: boolean
  value: 2 | 3
}

export interface InviteDto {
  inviteCode: string
  expiresAt: string
}

export interface CreateShotDto {
  gameStatsId: number
  quarter: number
  x: number
  y: number
  made: boolean
  value: 2 | 3
}
