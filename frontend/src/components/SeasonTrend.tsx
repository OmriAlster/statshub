import { Link } from 'react-router-dom'
import type { GameDto } from '../api/types'

// The season so far, game by game (games with the player's line, oldest
// first): points per game as a line, how the shooting changed, and the
// player's bests. showTypes: the games are of more than one kind (league,
// cup) - each dot in its kind's color.
export default function SeasonTrend({ games, playerId, showTypes = false }: { games: GameDto[]; playerId: string; showTypes?: boolean }) {
  const played = games
    .filter((g) => g.status === 'Completed' && g.playerStats.length > 0)
    .sort((a, b) => new Date(a.gameDate).getTime() - new Date(b.gameDate).getTime())
  if (played.length === 0) return null
  const kinds = [...new Set(played.map((g) => g.gameType))]

  const line = (g: GameDto) => g.playerStats[0]
  const points = played.map((g) => line(g).totalPoints)

  // Shooting (field goals, 2s and 3s): the first half of the games against the second.
  const shooting = (list: GameDto[]) => {
    const made = list.reduce((s, g) => s + line(g).fieldGoalsMade + line(g).threePointersMade, 0)
    const tried = list.reduce((s, g) => s + line(g).fieldGoalsAttempted + line(g).threePointersAttempted, 0)
    return tried > 0 ? Math.round((made / tried) * 100) : null
  }
  const half = Math.floor(played.length / 2)
  const [before, after] = [shooting(played.slice(0, half)), shooting(played.slice(half))]

  const recent = points.slice(-3)
  const earlier = points.slice(0, -3)
  const avg = (xs: number[]) => xs.reduce((s, x) => s + x, 0) / xs.length
  const change = earlier.length > 0 ? avg(recent) - avg(earlier) : null

  // The chart
  const w = 320
  const h = 120
  const pad = 14
  const max = Math.max(...points, 10)
  const x = (i: number) => (points.length === 1 ? w / 2 : pad + (i * (w - pad * 2)) / (points.length - 1))
  const y = (p: number) => h - pad - (p / max) * (h - pad * 2)
  const path = points.map((p, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)} ${y(p).toFixed(1)}`).join(' ')
  const best = Math.max(...points)

  const bests = [
    { label: 'Points', value: (g: GameDto) => line(g).totalPoints },
    { label: 'Rebounds', value: (g: GameDto) => line(g).totalRebounds },
    { label: 'Assists', value: (g: GameDto) => line(g).assists },
    { label: '3-pointers', value: (g: GameDto) => line(g).threePointersMade },
    { label: 'Steals', value: (g: GameDto) => line(g).steals },
  ].map((b) => {
    const top = played.reduce((t, g) => (b.value(g) >= b.value(t) ? g : t), played[0])
    return { ...b, game: top, n: b.value(top), latest: top.id === played[played.length - 1].id }
  }).filter((b) => b.n > 0)

  return (
    <div className="season-trend">
      <section className="trend-card" aria-labelledby="trend-title">
        <div className="trend-head">
          <h3 id="trend-title">{played.length === 1 ? 'Points' : 'Points, game by game'}</h3>
          {change != null && Math.abs(change) >= 0.5 && (
            <span className={`trend-change ${change > 0 ? 'up' : 'down'}`}>{change > 0 ? '▲' : '▼'} {Math.abs(change).toFixed(1)} last 3</span>
          )}
        </div>
        <svg viewBox={`0 0 ${w} ${h}`} className="trend-chart" role="img" aria-label={`Points per game: ${points.join(', ')}`}>
          {[0.25, 0.5, 0.75].map((f) => <line key={f} x1={0} x2={w} y1={pad + f * (h - pad * 2)} y2={pad + f * (h - pad * 2)} className="trend-grid" />)}
          <path d={`${path} L${x(points.length - 1)} ${h - pad} L${x(0)} ${h - pad} Z`} className="trend-area" />
          <path d={path} className="trend-line" />
          {points.map((p, i) => (
            <circle key={i} cx={x(i)} cy={y(p)} r={i === points.length - 1 ? 4.5 : 3} className={`${i === points.length - 1 ? 'trend-last' : 'trend-dot'}${showTypes ? ` kind-${played[i].gameType.toLowerCase()}` : ''}`}>
              <title>{`${new Date(played[i].gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })} vs ${played[i].opponentName}: ${p}`}</title>
            </circle>
          ))}
          <text x={x(points.indexOf(best))} y={y(best) - 7} textAnchor="middle" className="trend-best">{best}</text>
        </svg>
        {showTypes && kinds.length > 1 && (
          <div className="trend-legend">
            {kinds.map((k) => <span key={k} className={`kind-${k.toLowerCase()}`}>{k}</span>)}
          </div>
        )}
        <div className="trend-axis">
          <span>{new Date(played[0].gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}</span>
          <span>{new Date(played[played.length - 1].gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}</span>
        </div>
        {before != null && after != null && (
          <p className="trend-shooting">Shooting: {before}% → <b className={after >= before ? 'up' : 'down'}>{after}%</b> <span>(first half of the season vs. since)</span></p>
        )}
      </section>

      {bests.length > 0 && (
        <section className="trend-card" aria-labelledby="bests-title">
          <h3 id="bests-title">Season bests</h3>
          <ul className="bests">
            {bests.map((b) => (
              <li key={b.label}>
                <span>{b.label}</span>
                <Link to={`/games/${b.game.id}?playerId=${playerId}`} title={`vs ${b.game.opponentName}`}>
                  <b>{b.n}</b>
                  <span className="bests-vs" dir="auto">vs {b.game.opponentName}</span>
                  {b.latest && <span className="bests-new">NEW</span>}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}

// The season by kind of game - league, cup, friendly side by side (friendlies
// too: shown here, never mixed into the totals). A row opens that kind's view.
export function CompetitionSplit({ games, onPick }: { games: GameDto[]; onPick: (type: GameDto['gameType']) => void }) {
  const played = games.filter((g) => g.status === 'Completed' && g.playerStats.length > 0)
  const rows = (['League', 'Cup', 'Friendly'] as const)
    .map((type) => {
      const list = played.filter((g) => g.gameType === type)
      const sum = (f: (s: GameDto['playerStats'][number]) => number) => list.reduce((t, g) => t + f(g.playerStats[0]), 0)
      const per = (n: number) => (n / list.length).toFixed(1)
      const shots = sum((s) => s.fieldGoalsAttempted + s.threePointersAttempted)
      const made = sum((s) => s.fieldGoalsMade + s.threePointersMade)
      const wins = list.filter((g) => (g.teamScore ?? 0) > (g.opponentScore ?? 0)).length
      return {
        type,
        games: list.length,
        record: `${wins}-${list.length - wins}`,
        ppg: list.length ? per(sum((s) => s.totalPoints)) : '-',
        rpg: list.length ? per(sum((s) => s.totalRebounds)) : '-',
        apg: list.length ? per(sum((s) => s.assists)) : '-',
        fg: shots ? `${Math.round((made / shots) * 100)}%` : '-',
      }
    })
    .filter((r) => r.games > 0)
  if (rows.length < 2) return null

  return (
    <section className="trend-card competition-split" aria-labelledby="split-title">
      <h3 id="split-title">By competition</h3>
      <div className="split-table">
        <div className="split-row split-head">
          <span />
          <span>GP</span>
          <span>W-L</span>
          <span>PPG</span>
          <span>RPG</span>
          <span>APG</span>
          <span>FG%</span>
        </div>
        {rows.map((r) => (
          <button type="button" key={r.type} className="split-row" onClick={() => onPick(r.type)} title={`Only ${r.type.toLowerCase()} games`}>
            <span className={`split-kind kind-${r.type.toLowerCase()}`}>{r.type}</span>
            <span>{r.games}</span>
            <span>{r.record}</span>
            <b>{r.ppg}</b>
            <span>{r.rpg}</span>
            <span>{r.apg}</span>
            <span>{r.fg}</span>
          </button>
        ))}
      </div>
      <p className="split-note">Friendlies are shown here, but not counted in the season's totals.</p>
    </section>
  )
}
