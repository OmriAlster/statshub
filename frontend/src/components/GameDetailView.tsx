import type { ReactNode } from 'react'
import type { GameDto } from '../api/types'
import { formatGameDateTimeFull } from '../utils/formatGameDate'
import CourtShotChart from './CourtShotChart'
import GameStatusBadge, { onCourtIn } from './GameStatusBadge'
import LeagueBadge from './LeagueBadge'
import NavigateButtons from './NavigateButtons'

interface GameDetailViewProps {
  game: GameDto
  headerActions?: ReactNode
}

// Pure, read-only rendering of a game's score and box score(s) - used by both
// the authenticated Game Detail page and the public shared-game page, so the
// two are guaranteed to look identical rather than two hand-maintained
// almost-the-same designs. Editing lives only in the Schedule tab now.
export default function GameDetailView({ game, headerActions }: GameDetailViewProps) {
  const won = (game.teamScore ?? 0) > (game.opponentScore ?? 0)

  return (
    <div>
      <div className="game-detail-header">
        <div>
          <div className="game-meta-row">
            <span className={`game-type-badge ${game.gameType.toLowerCase()}`}>{game.gameType}</span>
            {game.isFromIbba && <LeagueBadge league={game.league} />}
          </div>
          <p>{formatGameDateTimeFull(game.gameDate)}</p>
          <p className="game-venue">
            {game.isHomeGame != null && <span className="game-venue-side">{game.isHomeGame ? '🏠 Home' : '✈️ Away'}</span>}
            <span className="game-venue-place">📍 <bdi>{game.location || 'TBD'}</bdi></span>
          </p>
        </div>
        {headerActions && <div className="flex gap-1">{headerActions}</div>}
      </div>

      {game.status !== 'Completed' && <NavigateButtons location={game.location} />}

      {game.status === 'Completed' ? (
        <div className="game-score">
          <div className="score-board-head">
            <span className={`score-board-result ${won ? 'win' : 'loss'}`}>{won ? 'Win' : 'Loss'}</span>
            <span className="score-board-final">Final</span>
          </div>
          <ScoreBoard game={game} won={won} />
        </div>
      ) : (
        <div className="game-upcoming">
          <p><GameStatusBadge status={game.status} onCourt={onCourtIn(game)} /></p>
          <ScoreBoard game={game} />
        </div>
      )}

      {game.notes && (
        <div className="info-section" style={{ marginTop: '1.5rem' }}>
          <p>{game.notes}</p>
        </div>
      )}

      {game.playerStats.map((stats) => (
        <div key={stats.id} className="stats-card-enhanced" style={{ marginTop: '1.75rem' }}>
          <div className="player-header-enhanced">
            <div className="player-info">
              <div>
                <h3>{stats.playerName}</h3>
                <p>{stats.minutesPlayed} minutes played</p>
              </div>
            </div>
          </div>

          <div className="primary-stats">
            <h4>Box Score</h4>
            <div className="stats-grid-enhanced">
              <div className="stat-box-enhanced featured">
                <span className="stat-value">{stats.totalPoints}</span>
                <span className="stat-label">PTS</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.totalRebounds}</span>
                <span className="stat-label">REB</span>
                <span className="stat-detail">{stats.offensiveRebounds} off · {stats.defensiveRebounds} def</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.assists}</span>
                <span className="stat-label">AST</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.steals}</span>
                <span className="stat-label">STL</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.blocks}</span>
                <span className="stat-label">BLK</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.turnovers}</span>
                <span className="stat-label">TO</span>
              </div>
              <div className="stat-box-enhanced">
                <span className="stat-value">{stats.fouls}</span>
                <span className="stat-label">FOULS</span>
              </div>
            </div>
          </div>

          <div className="shooting-stats">
            <h4>Shooting</h4>
            <div className="percentage-bars">
              <div className="percentage-item">
                <div className="percentage-label">2PT {stats.fieldGoalsMade}/{stats.fieldGoalsAttempted}</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${stats.fieldGoalPercentage}%` }} /></div>
                <span className="percentage-value">{stats.fieldGoalPercentage}%</span>
              </div>
              <div className="percentage-item">
                <div className="percentage-label">3PT {stats.threePointersMade}/{stats.threePointersAttempted}</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${stats.threePointPercentage}%` }} /></div>
                <span className="percentage-value">{stats.threePointPercentage}%</span>
              </div>
              <div className="percentage-item">
                <div className="percentage-label">FT {stats.freeThrowsMade}/{stats.freeThrowsAttempted}</div>
                <div className="percentage-bar"><div className="percentage-fill" style={{ width: `${stats.freeThrowPercentage}%` }} /></div>
                <span className="percentage-value">{stats.freeThrowPercentage}%</span>
              </div>
            </div>
          </div>

          <div className="season-chart-section">
            <h4>🎯 Shot Chart</h4>
            {(stats.shots?.length ?? 0) === 0 ? (
              <p className="no-shots-note">No shots logged for this game.</p>
            ) : (
              <CourtShotChart shots={stats.shots!} interactive={false} />
            )}
          </div>
        </div>
      ))}
    </div>
  )
}

// The two teams, a row each - crest, the whole name (two lines if it needs
// them) and the score once there is one. Side by side, long names were cut
// to a few letters on a phone.
function ScoreBoard({ game, won }: { game: GameDto; won?: boolean }) {
  const played = game.status !== 'Upcoming' && game.teamScore != null
  const rows = [
    { name: game.teamName, logo: game.teamLogoUrl, score: game.teamScore, wonRow: won === true },
    { name: game.opponentName, logo: game.opponentLogoUrl, score: game.opponentScore, wonRow: won === false },
  ]
  return (
    <div className="score-board">
      {rows.map((r, i) => (
        <div key={i} className={`score-board-row ${played && r.wonRow ? 'won' : ''}`}>
          {r.logo ? <img className="score-board-crest" src={r.logo} alt="" /> : <span className="score-board-crest blank" aria-hidden="true" />}
          <span className="score-board-name" dir="auto">{r.name || 'TBD'}</span>
          {played && <b className="score-board-score">{r.score}</b>}
        </div>
      ))}
    </div>
  )
}
