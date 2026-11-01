import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import type { GameDto } from '../api/types'
import { countsTowardStats } from '../utils/countedGames'
import { formatGameTime } from '../utils/formatGameDate'
import GameStatusBadge, { onCourtIn } from './GameStatusBadge'
import { GameTypeBadge, HomeAway } from './GameBadges'
import { madeOf, sheetOff, show } from '../utils/statLines'
import SheetMismatch from './SheetMismatch'

// Phone layout for the Stats and Schedule tables: a 5-13 column table can't
// fit a phone screen (only Opponent and Date were visible, everything else
// hidden off to the side), so on small screens each game is a card instead.
// The table stays for wider screens - see .mobile-only / .desktop-only.

export interface GameAverages {
  count: number
  pts: string
  fgm: string
  fga: string
  tpm: string
  tpa: string
  ftm: string
  fta: string
  reb: string
  ast: string
  stl: string
  blk: string
  to: string
}

function DateBlock({ iso, withTime }: { iso: string; withTime: boolean }) {
  const date = new Date(iso)
  return (
    <div className="game-card-date" aria-hidden="true">
      <span className="gcd-month">{date.toLocaleDateString('en-US', { month: 'short' })}</span>
      <span className="gcd-day">{date.getDate()}</span>
      <span className="gcd-sub">{withTime ? formatGameTime(iso) : date.toLocaleDateString('en-US', { weekday: 'short' })}</span>
    </div>
  )
}

function Matchup({ game, counted = countsTowardStats(game) }: { game: GameDto; counted?: boolean }) {
  return (
    <>
      <div className="game-card-opponent">
        {game.opponentLogoUrl && <img className="opponent-logo-sm" src={game.opponentLogoUrl} alt="" />}
        <span>{game.opponentName}</span>
      </div>
      <div className="game-card-meta">
        <HomeAway game={game} />
        <GameTypeBadge game={game} />
        {game.status === 'Completed' && !counted && <span className="not-counted-note">not counted</span>}
      </div>
    </>
  )
}

function Result({ game }: { game: GameDto }) {
  if (game.status !== 'Completed') return <GameStatusBadge status={game.status} onCourt={onCourtIn(game)} />
  const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)
  return (
    <span className={`game-card-score ${won ? 'win' : 'loss'}`}>
      {won ? 'W' : 'L'} {game.teamScore}&ndash;{game.opponentScore}
    </span>
  )
}

// Completed games with this player's box score (Stats tab).
export function StatsGameCards({
  games,
  linkFor,
  isCounted = countsTowardStats,
}: {
  games: GameDto[]
  linkFor: (game: GameDto) => string
  // Which games count toward the numbers - friendlies don't, unless they're
  // what the game-type filter is showing.
  isCounted?: (game: GameDto) => boolean
}) {
  return (
    <ul className="game-cards mobile-only">
      {games.map((game) => {
        const stats = game.playerStats[0]
        const counted = isCounted(game)
        const off = sheetOff(stats)
        return (
          <li key={game.id}>
            <Link to={linkFor(game)} className={`game-card ${counted ? '' : 'not-counted'}`}>
              <div className="game-card-head">
                <DateBlock iso={game.gameDate} withTime={false} />
                <div className="game-card-main">
                  <Matchup game={game} counted={counted} />
                </div>
                <span className="game-card-end">
                  <Result game={game} />
                  <SheetMismatch stats={stats} />
                </span>
              </div>
              {stats ? (
                <>
                  <div className="game-card-stats">
                    <div className={`gcs-pts ${off('points')}`}><b>{stats.totalPoints}</b><span>PTS</span></div>
                    <div><b>{show(stats.totalRebounds)}</b><span>REB</span></div>
                    <div><b>{show(stats.assists)}</b><span>AST</span></div>
                    <div><b>{show(stats.steals)}</b><span>STL</span></div>
                    <div><b>{show(stats.blocks)}</b><span>BLK</span></div>
                    <div><b>{show(stats.turnovers)}</b><span>TO</span></div>
                  </div>
                  <div className="game-card-shooting">
                    <span className={off('two')}>2PT <b>{madeOf(stats.fieldGoalsMade, stats.fieldGoalsAttempted)}</b></span>
                    <span className={off('three')}>3PT <b>{madeOf(stats.threePointersMade, stats.threePointersAttempted)}</b></span>
                    <span className={off('ft')}>FT <b>{madeOf(stats.freeThrowsMade, stats.freeThrowsAttempted)}</b></span>
                    {stats.source && <span className="stats-source">{stats.source === 'scoresheet' ? 'Scoresheet' : 'IBBA'}</span>}
                  </div>
                </>
              ) : (
                <p className="game-card-empty">No box score recorded</p>
              )}
            </Link>
          </li>
        )
      })}
    </ul>
  )
}

// Every game, played or not (Schedule tab). actions renders per-game buttons
// (edit/delete) on the authenticated page; the public share page passes a
// link instead.
export function ScheduleGameCards({
  games,
  actions,
  linkFor,
}: {
  games: GameDto[]
  actions?: (game: GameDto) => ReactNode
  linkFor?: (game: GameDto) => string
}) {
  return (
    <ul className="game-cards mobile-only">
      {games.map((game) => {
        const body = (
          <div className="game-card-head">
            <DateBlock iso={game.gameDate} withTime />
            <div className="game-card-main">
              <Matchup game={game} />
            </div>
            <div className="game-card-side">
              <Result game={game} />
              {actions && <div className="game-card-actions">{actions(game)}</div>}
            </div>
          </div>
        )
        const className = `game-card ${game.status !== 'Completed' ? 'is-upcoming' : ''}`
        return (
          <li key={game.id}>
            {linkFor ? <Link to={linkFor(game)} className={className}>{body}</Link> : <div className={className}>{body}</div>}
          </li>
        )
      })}
    </ul>
  )
}

// The table's averages footer, as its own card under the game cards.
export function AveragesCard({ averages }: { averages: GameAverages }) {
  return (
    <div className="game-averages-card mobile-only">
      <div className="gac-title">Averages <span>· {averages.count} {averages.count === 1 ? 'game' : 'games'}</span></div>
      <div className="game-card-stats">
        <div className="gcs-pts"><b>{averages.pts}</b><span>PTS</span></div>
        <div><b>{averages.reb}</b><span>REB</span></div>
        <div><b>{averages.ast}</b><span>AST</span></div>
        <div><b>{averages.stl}</b><span>STL</span></div>
        <div><b>{averages.blk}</b><span>BLK</span></div>
        <div><b>{averages.to}</b><span>TO</span></div>
      </div>
      <div className="game-card-shooting">
        <span>2PT <b>{averages.fgm}/{averages.fga}</b></span>
        <span>3PT <b>{averages.tpm}/{averages.tpa}</b></span>
        <span>FT <b>{averages.ftm}/{averages.fta}</b></span>
      </div>
    </div>
  )
}
