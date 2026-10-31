import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { GameDto, GameStatsDto } from '../api/types'
import { playedGames } from '../utils/countedGames'

// The player's profile at a glance - what's built from results alone (the
// next game, the team's form, home and away) works for every player; the
// rest needs the player's own lines (tracked live, or a league that
// publishes them).

const won = (g: GameDto) => (g.teamScore ?? 0) > (g.opponentScore ?? 0)
const byDate = (a: GameDto, b: GameDto) => new Date(a.gameDate).getTime() - new Date(b.gameDate).getTime()
const lineOf = (g: GameDto, playerId: string): GameStatsDto | undefined => g.playerStats.find((s) => s.playerId === playerId) ?? g.playerStats[0]


// ---- The next game -------------------------------------------------------

interface MatchupTeam { name: string; logoUrl?: string | null; position: number; totalTeams?: number | null; wins: number; losses: number }
interface Matchup { opponent?: MatchupTeam | null; previous: { gameId: string; teamScore?: number | null; opponentScore?: number | null }[] }

function when(date: Date) {
  const days = Math.round((new Date(date).setHours(0, 0, 0, 0) - new Date().setHours(0, 0, 0, 0)) / 86_400_000)
  const time = date.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
  if (days <= 0) return `Today · ${time}`
  if (days === 1) return `Tomorrow · ${time}`
  if (days < 7) return `${date.toLocaleDateString('en-US', { weekday: 'long' })} · ${time}`
  return `In ${days} days`
}

export function NextGameCard({ games, playerId }: { games: GameDto[]; playerId: string }) {
  const next = games.filter((g) => g.status === 'Upcoming' && new Date(g.gameDate).getTime() > Date.now() - 3 * 3_600_000).sort(byDate)[0]
  const [matchup, setMatchup] = useState<Matchup | null>(null)

  useEffect(() => {
    if (!next) return
    let current = true
    api.get<Matchup>(`/games/${next.id}/matchup`, { params: { playerId } })
      .then((res) => current && setMatchup(res.data))
      .catch(() => current && setMatchup(null))
    return () => { current = false }
  }, [next?.id, playerId]) // eslint-disable-line react-hooks/exhaustive-deps

  if (!next) return null
  const opp = matchup?.opponent
  const last = matchup?.previous[matchup.previous.length - 1]
  const logo = next.opponentLogoUrl ?? opp?.logoUrl

  return (
    <Link to={`/games/${next.id}?playerId=${playerId}`} className="insight-card next-game-card">
      <div className="insight-head"><span className="insight-label">Next game</span><span className="next-when">{when(new Date(next.gameDate))}</span></div>
      <div className="next-game-team">
        {logo ? <img src={logo} alt="" /> : <span className="next-crest-blank" aria-hidden="true" />}
        <div>
          <div className="next-opp" dir="auto">{next.opponentName}</div>
          <div className="next-meta">
            {next.isHomeGame != null && <span>{next.isHomeGame ? '🏠 Home' : '✈️ Away'}</span>}
            {opp && opp.position > 0 && <span>{opp.position}{opp.totalTeams ? ` of ${opp.totalTeams}` : ''} · {opp.wins}-{opp.losses}</span>}
            {last && last.teamScore != null && (
              <span>Last time: <b className={(last.teamScore ?? 0) > (last.opponentScore ?? 0) ? 'win' : 'loss'}>{(last.teamScore ?? 0) > (last.opponentScore ?? 0) ? 'W' : 'L'} {last.teamScore}-{last.opponentScore}</b></span>
            )}
          </div>
        </div>
      </div>
    </Link>
  )
}

// ---- The team's form -------------------------------------------------------

export function FormCard({ games }: { games: GameDto[] }) {
  const played = playedGames(games)
  if (played.length === 0) return null
  const last5 = played.slice(-5)
  let streak = 0
  const lastWon = won(played[played.length - 1])
  for (let i = played.length - 1; i >= 0 && won(played[i]) === lastWon; i--) streak++

  return (
    <section className="insight-card" aria-label="Team form">
      <div className="insight-head">
        <span className="insight-label">Form · last {last5.length}</span>
        {streak >= 2 && <span className={`form-streak ${lastWon ? 'win' : 'loss'}`}>{lastWon ? `🔥 ${streak} wins in a row` : `${streak} losses in a row`}</span>}
      </div>
      <div className="form-boxes">
        {last5.map((g) => (
          <span key={g.id} className={won(g) ? 'w' : 'l'} title={`${new Date(g.gameDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })} vs ${g.opponentName}: ${g.teamScore}-${g.opponentScore}`}>
            {won(g) ? 'W' : 'L'}
          </span>
        ))}
      </div>
    </section>
  )
}

// ---- Home and away -----------------------------------------------------------

export function HomeAwayCard({ games, playerId }: { games: GameDto[]; playerId: string }) {
  const played = playedGames(games)
  const side = (home: boolean) => {
    const list = played.filter((g) => g.isHomeGame === home)
    const wins = list.filter(won).length
    const margin = list.length ? list.reduce((s, g) => s + (g.teamScore! - g.opponentScore!), 0) / list.length : 0
    const lines = list.map((g) => lineOf(g, playerId)).filter((l): l is GameStatsDto => !!l)
    const ppg = lines.length ? lines.reduce((s, l) => s + l.totalPoints, 0) / lines.length : null
    return { games: list.length, wins, margin, ppg }
  }
  const [home, away] = [side(true), side(false)]
  if (home.games === 0 || away.games === 0) return null

  const box = (label: string, s: ReturnType<typeof side>) => (
    <div className="ha-box">
      <span className="insight-label">{label}</span>
      <b className={s.wins * 2 >= s.games ? 'win' : 'loss'}>{s.wins}-{s.games - s.wins}</b>
      <span>{s.margin >= 0 ? '+' : '−'}{Math.abs(s.margin).toFixed(1)} per game</span>
      {s.ppg != null && <span>{s.ppg.toFixed(1)} PPG</span>}
    </div>
  )
  return (
    <section className="insight-card" aria-label="Home and away">
      <div className="ha-grid">{box('🏠 Home', home)}{box('✈️ Away', away)}</div>
    </section>
  )
}

// ---- Personal streaks ----------------------------------------------------------

const STREAKS: { label: string; hit: (l: GameStatsDto) => boolean }[] = [
  { label: '20+ points', hit: (l) => l.totalPoints >= 20 },
  { label: '10+ points', hit: (l) => l.totalPoints >= 10 },
  { label: 'a 3-pointer', hit: (l) => l.threePointersMade > 0 },
  { label: '5+ rebounds', hit: (l) => l.totalRebounds >= 5 },
  { label: 'an assist', hit: (l) => l.assists > 0 },
  { label: 'a steal', hit: (l) => l.steals > 0 },
]

export function StreaksCard({ games, playerId }: { games: GameDto[]; playerId: string }) {
  const lines = playedGames(games).map((g) => lineOf(g, playerId)).filter((l): l is GameStatsDto => !!l)
  if (lines.length < 2) return null

  const streaks = STREAKS.map((s) => {
    let now = 0
    for (let i = lines.length - 1; i >= 0 && s.hit(lines[i]); i--) now++
    let best = 0
    let run = 0
    for (const l of lines) { run = s.hit(l) ? run + 1 : 0; best = Math.max(best, run) }
    return { ...s, now, best }
  })
  // Going on now first (the longest), then the season's longest ones.
  const shown = [
    ...streaks.filter((s) => s.now >= 2).sort((a, b) => b.now - a.now),
    ...streaks.filter((s) => s.now < 2 && s.best >= 3).sort((a, b) => b.best - a.best),
  ].slice(0, 3)
  if (shown.length === 0) return null
  const top = shown[0]
  const recent = lines.slice(-8)

  return (
    <section className="insight-card" aria-label="Streaks">
      <div className="insight-head"><span className="insight-label">Streaks</span></div>
      <ul className="streak-list">
        {shown.map((s) => (
          <li key={s.label}>
            {s.now >= 2 ? <span className="streak-flame" aria-hidden="true">🔥</span> : <span className="streak-flame dim" aria-hidden="true">🏅</span>}
            <span>{s.now >= 2 ? <><b>{s.now}</b> straight games with {s.label}</> : <>Season best: <b>{s.best}</b> straight with {s.label}</>}</span>
            {s.now >= 2 && s.best > s.now && <span className="streak-best">best {s.best}</span>}
          </li>
        ))}
      </ul>
      <div className="streak-dots" aria-hidden="true">
        {recent.map((l, i) => <span key={i} className={top.hit(l) ? 'hit' : ''}>{l.totalPoints}</span>)}
      </div>
    </section>
  )
}

// ---- His share of the team's points --------------------------------------------

export function TeamShareCard({ games, playerId }: { games: GameDto[]; playerId: string }) {
  const rows = playedGames(games)
    .map((g) => ({ g, l: lineOf(g, playerId) }))
    .filter((r): r is { g: GameDto; l: GameStatsDto } => !!r.l && (r.g.teamScore ?? 0) > 0)
  if (rows.length === 0) return null
  const mine = rows.reduce((s, r) => s + r.l.totalPoints, 0)
  const team = rows.reduce((s, r) => s + r.g.teamScore!, 0)
  const share = Math.round((mine / team) * 100)
  const best = rows.reduce((b, r) => (r.l.totalPoints / r.g.teamScore! > b.l.totalPoints / b.g.teamScore! ? r : b), rows[0])

  return (
    <section className="insight-card" aria-label="Share of the team's points">
      <div className="insight-head"><span className="insight-label">Share of team points</span><b className="share-pct">{share}%</b></div>
      <div className="share-track"><b style={{ width: `${Math.min(share, 100)}%` }} /></div>
      <p className="insight-note">{mine} of the team's {team} points in {rows.length} game{rows.length === 1 ? '' : 's'}</p>
      {rows.length > 1 && (
        <p className="insight-note">
          Most: <Link to={`/games/${best.g.id}?playerId=${playerId}`}>{Math.round((best.l.totalPoints / best.g.teamScore!) * 100)}% vs <bdi>{best.g.opponentName}</bdi></Link>
        </p>
      )}
    </section>
  )
}

// ---- Season goals ------------------------------------------------------------------

interface Goal { stat: GoalStat; target: number }
type GoalStat = 'ppg' | 'rpg' | 'apg' | 'spg' | 'fgPct' | 'threePct' | 'ftPct'

const GOALS: { stat: GoalStat; label: string; unit: string; max: number; step: number }[] = [
  { stat: 'ppg', label: 'Points per game', unit: '', max: 60, step: 0.5 },
  { stat: 'rpg', label: 'Rebounds per game', unit: '', max: 30, step: 0.5 },
  { stat: 'apg', label: 'Assists per game', unit: '', max: 30, step: 0.5 },
  { stat: 'spg', label: 'Steals per game', unit: '', max: 20, step: 0.5 },
  { stat: 'fgPct', label: 'Field goals', unit: '%', max: 100, step: 1 },
  { stat: 'threePct', label: '3-pointers', unit: '%', max: 100, step: 1 },
  { stat: 'ftPct', label: 'Free throws', unit: '%', max: 100, step: 1 },
]

function current(stat: GoalStat, lines: GameStatsDto[]): { value: number; detail: string } | null {
  if (lines.length === 0) return null
  const sum = (f: (l: GameStatsDto) => number) => lines.reduce((s, l) => s + f(l), 0)
  const per = (n: number) => ({ value: n / lines.length, detail: `${n} in ${lines.length} game${lines.length === 1 ? '' : 's'}` })
  const pct = (made: number, tried: number) => (tried === 0 ? null : { value: (made / tried) * 100, detail: `${made} of ${tried}` })
  switch (stat) {
    case 'ppg': return per(sum((l) => l.totalPoints))
    case 'rpg': return per(sum((l) => l.totalRebounds))
    case 'apg': return per(sum((l) => l.assists))
    case 'spg': return per(sum((l) => l.steals))
    case 'fgPct': return pct(sum((l) => l.fieldGoalsMade + l.threePointersMade), sum((l) => l.fieldGoalsAttempted + l.threePointersAttempted))
    case 'threePct': return pct(sum((l) => l.threePointersMade), sum((l) => l.threePointersAttempted))
    case 'ftPct': return pct(sum((l) => l.freeThrowsMade), sum((l) => l.freeThrowsAttempted))
  }
}

function Ring({ fraction, text }: { fraction: number; text: string }) {
  const c = 2 * Math.PI * 26
  return (
    <svg className="goal-ring" viewBox="0 0 64 64" aria-hidden="true">
      <circle cx="32" cy="32" r="26" className="goal-ring-track" />
      <circle cx="32" cy="32" r="26" className={`goal-ring-fill ${fraction >= 1 ? 'done' : ''}`} strokeDasharray={c} strokeDashoffset={c * (1 - Math.min(fraction, 1))} transform="rotate(-90 32 32)" />
      <text x="32" y="37" textAnchor="middle">{text}</text>
    </svg>
  )
}

export function GoalsCard({ games, playerId, readOnly = false }: { games: GameDto[]; playerId: string; readOnly?: boolean }) {
  const [goals, setGoals] = useState<Goal[] | null>(null)
  const [editing, setEditing] = useState<Record<string, string> | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let live = true
    api.get<Goal[]>(`/players/${playerId}/goals`).then((res) => live && setGoals(res.data)).catch(() => live && setGoals([]))
    return () => { live = false }
  }, [playerId])

  if (goals === null || (readOnly && goals.length === 0)) return null
  const lines = playedGames(games).map((g) => lineOf(g, playerId)).filter((l): l is GameStatsDto => !!l)
  const fmt = (stat: GoalStat, n: number) => (GOALS.find((x) => x.stat === stat)!.unit === '%' ? `${Math.round(n)}%` : n.toFixed(1))

  const startEdit = () => setEditing(Object.fromEntries(goals.map((g) => [g.stat, String(g.target)])))
  const save = async () => {
    const list = Object.entries(editing ?? {})
      .filter(([, v]) => v.trim() !== '' && Number(v) > 0)
      .map(([stat, v]) => ({ stat: stat as GoalStat, target: Number(v) }))
    setSaving(true)
    try {
      const { data } = await api.put<Goal[]>(`/players/${playerId}/goals`, list)
      setGoals(data)
      setEditing(null)
      setError(null)
    } catch {
      setError('Could not save the goals. Check the numbers and try again.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="insight-card goals-card" aria-labelledby="goals-title">
      <div className="insight-head">
        <h3 id="goals-title" className="insight-label">Season goals</h3>
        {!editing && !readOnly && <button type="button" className="insight-link" onClick={startEdit}>{goals.length ? 'Edit' : '+ Set a goal'}</button>}
      </div>

      {editing ? (
        <div className="goal-editor">
          {GOALS.map((g) => (
            <label key={g.stat}>
              <span>{g.label}</span>
              <span className="goal-input">
                <input
                  type="number"
                  inputMode="decimal"
                  min={0}
                  max={g.max}
                  step={g.step}
                  placeholder="-"
                  value={editing[g.stat] ?? ''}
                  onChange={(e) => setEditing({ ...editing, [g.stat]: e.target.value })}
                />
                {g.unit}
              </span>
            </label>
          ))}
          {error && <p className="error">{error}</p>}
          <div className="goal-actions">
            <button type="button" className="nav-btn" onClick={() => { setEditing(null); setError(null) }}>Cancel</button>
            <button type="button" className="submit-btn" disabled={saving} onClick={save}>{saving ? 'Saving…' : 'Save goals'}</button>
          </div>
        </div>
      ) : goals.length === 0 ? (
        <p className="insight-note">Pick a target for the season, like 10 points a game or 70% from the line, and watch it fill up after every game.</p>
      ) : (
        <ul className="goal-list">
          {goals.map((goal) => {
            const meta = GOALS.find((x) => x.stat === goal.stat)!
            const now = current(goal.stat, lines)
            const fraction = now ? now.value / goal.target : 0
            return (
              <li key={goal.stat}>
                <Ring fraction={fraction} text={now ? `${Math.min(Math.round(fraction * 100), 999)}%` : '-'} />
                <div>
                  <b>{meta.label} {fmt(goal.stat, goal.target)}</b>
                  <span>{now ? `Now ${fmt(goal.stat, now.value)} · ${now.detail}` : 'No games with stats yet'}</span>
                  {fraction >= 1 && <span className="goal-done">Reached 🎉</span>}
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
