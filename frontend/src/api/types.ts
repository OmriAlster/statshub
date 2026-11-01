export interface TeamDto {
  id: string
  name: string
  // Only present when this team appears in a player-specific context (e.g.
  // PlayerDto.teams) - a player can wear a different number per team.
  jerseyNumber?: number | null
}

export interface ParentDto {
  userId: string
  firstName: string
  lastName: string
}

export interface PlayerDto {
  id: string
  firstName: string
  lastName: string
  position: string
  height?: number | null
  weight?: number | null
  dateOfBirth?: string | null // not every child has one on file
  profilePictureUrl?: string | null
  teams: TeamDto[]
  parents: ParentDto[]
  // The player's own login, once they've joined.
  linkedUser?: ParentDto | null
}

export interface UserDto {
  id: string
  email: string
  firstName: string
  lastName: string
  profilePictureUrl?: string | null
  role: 'Parent' | 'Player'
  linkedPlayer?: PlayerDto | null
}

export interface SeasonDto {
  id: string
  name: string
  sport: string
  year: number
  startDate: string
  endDate?: string | null
  totalGames: number
}

export interface GameStatsDto {
  id: string
  gameId: string
  playerId: string
  playerName: string
  // Where the numbers came from: null - the parent; 'box' - the league's box score;
  // 'scoresheet' - the game's scoresheet. A null stat wasn't recorded (shown as "-").
  source?: 'box' | 'scoresheet' | null
  fieldGoalsMade: number
  fieldGoalsAttempted: number | null
  fieldGoalPercentage: number | null
  threePointersMade: number
  threePointersAttempted: number | null
  threePointPercentage: number | null
  freeThrowsMade: number
  freeThrowsAttempted: number | null
  freeThrowPercentage: number | null
  offensiveRebounds: number | null
  defensiveRebounds: number | null
  totalRebounds: number | null
  assists: number | null
  steals: number | null
  blocks: number | null
  turnovers: number | null
  fouls: number
  minutesPlayed: number | null
  // Live game: on the court right now (as the tracker set it); null = not tracked.
  onCourt?: boolean | null
  totalPoints: number
  // The player's line on the game's scoresheet (a single game's view only).
  scoresheet?: ScoresheetLineDto | null
  // Only populated where a caller specifically asks for it (e.g. a shared game view).
  shots?: ShotDto[]
}

export interface ScoresheetLineDto {
  number: number
  starter: boolean
  points: number
  freeThrows: number
  twoPointers: number
  threePointers: number
  fouls: number
  // The sheet failed its own cross-checks - its numbers may be off.
  sheetValid: boolean
  sheetErrors?: string | null
}

export type GameType = 'League' | 'Cup' | 'Friendly'

export interface GameDto {
  id: string
  teamId: string
  teamName: string
  teamLogoUrl?: string | null
  gameType: GameType
  opponentName: string
  opponentLogoUrl?: string | null
  gameDate: string
  location: string
  status: 'Upcoming' | 'In Progress' | 'Completed'
  teamScore?: number | null
  opponentScore?: number | null
  notes?: string | null
  isHomeGame?: boolean | null
  isFromIbba: boolean
  // Which league it was synced from (a key from GET /api/leagues).
  league?: string | null
  canRecordLive: boolean
  playerStats: GameStatsDto[]
}

export interface CreateGameStatsDto {
  gameId: string
  playerId: string
  fieldGoalsMade: number
  fieldGoalsAttempted: number
  threePointersMade: number
  threePointersAttempted: number
  freeThrowsMade: number
  freeThrowsAttempted: number
  offensiveRebounds: number
  defensiveRebounds: number
  assists: number
  steals: number
  blocks: number
  turnovers: number
  fouls: number
  minutesPlayed: number
}

export type UpdateGameStatsDto = Partial<Omit<CreateGameStatsDto, 'gameId' | 'playerId'>>

export interface UpdateGameDto {
  opponentName?: string
  gameDate?: string
  location?: string
  status?: GameDto['status']
  gameType?: GameType
  teamScore?: number | null
  opponentScore?: number | null
  notes?: string
  isHomeGame?: boolean
}

export interface PlayerTeamStatsDto {
  playerId: string
  playerName: string
  jerseyNumber: number
  position: string
  teamId: string
  teamName: string
  gamesPlayed: number
  // A stat not recorded in every game is averaged over the games that have it;
  // null when none do.
  totalMinutes: number | null
  totalPoints: number
  pointsPerGame: number
  totalRebounds: number | null
  reboundsPerGame: number | null
  totalAssists: number | null
  assistsPerGame: number | null
  totalSteals: number | null
  stealsPerGame: number | null
  totalBlocks: number | null
  blocksPerGame: number | null
  totalTurnovers: number | null
  turnoversPerGame: number | null
  fieldGoalPercentage: number | null
  threePointPercentage: number | null
  freeThrowPercentage: number | null
}

export interface SharedTeamDto extends PlayerTeamStatsDto {
  logoUrl?: string | null
  isIbba: boolean
  league?: string | null
  leagueUrl?: string | null
  leagueName?: string | null
  standingPosition?: number | null
  standingTotalTeams?: number | null
}

export interface SharedPlayerDto {
  playerName: string
  position: string
  profilePictureUrl?: string | null
  game?: GameDto | null
  teams: SharedTeamDto[]
  games: GameDto[]
}

export interface ShotDto {
  id: string
  gameStatsId: string
  gameId: string
  playerId: string
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
  gameStatsId: string
  quarter: number
  x: number
  y: number
  made: boolean
  value: 2 | 3
}

export interface IbbaPreviewTeamDto {
  teamName: string
  teamUrl: string
  // A team the player may also play for, besides their main team.
  isAdditional: boolean
  // What the app already knows of the team - null until its first sync.
  logoUrl?: string | null
  leagueName?: string | null
  position?: number | null
  totalTeams?: number | null
}

export interface IbbaPreviewDto {
  leagueKey: string
  leagueName: string
  playerName: string
  // The player's page on the league's site - what adding or connecting them uses.
  playerUrl: string
  dateOfBirth?: string | null
  photoUrl?: string | null
  // One of your players is already connected to this profile.
  alreadyLinkedTo?: { playerId: string; playerName: string } | null
  teams: IbbaPreviewTeamDto[]
}

export interface IbbaTeamLinkDto {
  id: string
  teamName: string
  teamUrl: string
  teamLogoUrl?: string | null
  linkedTeamId?: string | null
  linkedTeamName?: string | null
  ibbaLeagueUrl?: string | null
  ibbaLeagueName?: string | null
  position?: number | null
  totalTeams?: number | null
}

// "Ibba" names are from when IBBA was the only league - these are for any.
export interface IbbaLinkStatusDto {
  leagueKey: string
  leagueName: string
  leagueLogoUrl: string
  playerId: string
  ibbaPlayerUrl: string
  lastSyncedAt?: string | null
  lastSyncError?: string | null
  // Games and standings still loading in the background after a link/sync.
  gamesLoading?: boolean
  teams: IbbaTeamLinkDto[]
  // The family's teams not linked to IBBA - offered in the "add to an
  // existing team?" pop-up.
  existingTeams?: TeamDto[]
}

export interface CreatePlayerFromIbbaDto {
  ibbaPlayerUrl: string
}

export interface CreatePlayerFromIbbaResultDto {
  player: PlayerDto
  ibba: IbbaLinkStatusDto | null
}

export interface IbbaStandingRowDto {
  position: number
  teamName: string
  teamUrl: string
  logoUrl?: string | null
  gamesPlayed: number
  wins: number
  losses: number
  technical: number
  pointsFor: number
  pointsAgainst: number
  diff: number
  leaguePoints: number
}

// ---- Family invites (/i/{code}) ----
export type FamilyInviteKind = 'Parent' | 'Player'

export interface InvitePlayerDto {
  id: string
  firstName: string
  lastName: string
  profilePictureUrl?: string | null
}

export interface FamilyInviteDto {
  id: string
  code: string
  kind: FamilyInviteKind
  expiresAt: string
  players: InvitePlayerDto[]
  joined: string[]
}

export interface InvitePreviewDto {
  kind: FamilyInviteKind
  invitedBy: string
  status: 'open' | 'expired' | 'used'
  players: InvitePlayerDto[]
}

export interface InviteAcceptResultDto {
  kind: FamilyInviteKind
  playerIds: string[]
}
