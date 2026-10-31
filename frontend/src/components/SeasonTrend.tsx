import { Link } from 'react-router-dom'
import type { GameDto } from '../api/types'

// The season so far, game by game (games with the player's line, oldest
// first): points per game as a line, how the shooting changed, and the
// player's bests.
export default function SeasonTrend({ games, playerId }: { games: GameDto[]; playerId: string }) {
  const played = games
    .filter((g) => g.status === 'Completed' && g.playerStats.length > 0)
    .sort((a, b) => new Date(a.gameDate).getTime() - new Date(b.gameDate).getTime())
  if (played.length < 2) return null

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
  const x = (i: number) => pad + (i * (w - pad * 2)) / (points.length - 1)
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
          <h3 id="trend-title">Points, game by game</h3>
          {change != null && Math.abs(change) >= 0.5 && (
            <span className={`trend-change ${change > 0 ? 'up' : 'down'}`}>{change > 0 ? '▲' : '▼'} {Math.abs(change).toFixed(1)} last 3</span>
          )}
        </div>
        <svg viewBox={`0 0 ${w} ${h}`} className="trend-chart" role="img" aria-label={`Points per game: ${points.join(', ')}`}>
          {[0.25, 0.5, 0.75].map((f) => <line key={f} x1={0} x2={w} y1={pad + f * (h - pad * 2)} y2={pad + f * (h - pad * 2)} className="trend-grid" />)}
          <path d={`${path} L${x(points.length - 1)} ${h - pad} L${x(0)} ${h - pad} Z`} className="trend-area" />
          <path d={path} className="trend-line" />
          {points.map((p, i) => (
            <circle key={i} cx={x(i)} cy={y(p)} r={i === points.length - 1 ? 4.5 : 3} className={i === points.length - 1 ? 'trend-last' : 'trend-dot'}>
              <title>{`${new Date(played[i].gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })} vs ${played[i].opponentName}: ${p}`}</title>
            </circle>
          ))}
          <text x={x(points.indexOf(best))} y={y(best) - 7} textAnchor="middle" className="trend-best">{best}</text>
        </svg>
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
