import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { GameStatsDto, UpdateGameStatsDto } from '../api/types'
import type { SaveState } from './GameStatsEditor'

type Field = keyof Pick<GameStatsDto,
  'fieldGoalsMade' | 'fieldGoalsAttempted' | 'threePointersMade' | 'threePointersAttempted' | 'freeThrowsMade' | 'freeThrowsAttempted'
  | 'offensiveRebounds' | 'defensiveRebounds' | 'assists' | 'steals' | 'blocks' | 'turnovers' | 'fouls' | 'minutesPlayed'>
type Values = Record<Field, number | null>

const FIELDS: Field[] = ['fieldGoalsMade', 'fieldGoalsAttempted', 'threePointersMade', 'threePointersAttempted', 'freeThrowsMade', 'freeThrowsAttempted',
  'offensiveRebounds', 'defensiveRebounds', 'assists', 'steals', 'blocks', 'turnovers', 'fouls', 'minutesPlayed']

const SHOTS: { label: string; made: Field; tried: Field; noun: string }[] = [
  { label: '2-pointers', made: 'fieldGoalsMade', tried: 'fieldGoalsAttempted', noun: '2-pointer' },
  { label: '3-pointers', made: 'threePointersMade', tried: 'threePointersAttempted', noun: '3-pointer' },
  { label: 'Free throws', made: 'freeThrowsMade', tried: 'freeThrowsAttempted', noun: 'free throw' },
]

const REST: { label: string; field: Field; noun: string; max?: number }[] = [
  { label: 'Assists', field: 'assists', noun: 'assist' },
  { label: 'Steals', field: 'steals', noun: 'steal' },
  { label: 'Blocks', field: 'blocks', noun: 'block' },
  { label: 'Turnovers', field: 'turnovers', noun: 'turnover' },
  { label: 'Fouls', field: 'fouls', noun: 'foul', max: 6 },
  { label: 'Minutes', field: 'minutesPlayed', noun: 'minute', max: 60 },
]

// The box score as numbers, one tap at a time - each change saved a moment
// later. A stat nobody recorded (null - a scoresheet's line has only the made
// shots and fouls) shows "Add" and stays unrecorded until it's added.
export default function NumbersEditor({ stats, onSaved, onSaveState }: {
  stats: GameStatsDto
  onSaved: (stats: GameStatsDto) => void
  onSaveState: (state: SaveState) => void
}) {
  const [values, setValues] = useState<Values>(() => Object.fromEntries(FIELDS.map((f) => [f, stats[f]])) as Values)
  const pending = useRef<UpdateGameStatsDto>({})
  const timer = useRef<number | null>(null)

  const flush = async () => {
    timer.current = null
    const dto = pending.current
    pending.current = {}
    if (Object.keys(dto).length === 0) return
    try {
      const { data } = await api.put<GameStatsDto>(`/gamestats/${stats.id}`, dto)
      onSaved(data)
      if (!timer.current) onSaveState('saved')
    } catch {
      onSaveState('error')
    }
  }

  // Leaving the editor saves what's still waiting.
  useEffect(() => () => {
    if (timer.current) { window.clearTimeout(timer.current); void flush() }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const change = (next: Partial<Values>) => {
    setValues((v) => ({ ...v, ...next }))
    Object.assign(pending.current, next)
    onSaveState('saving')
    if (timer.current) window.clearTimeout(timer.current)
    timer.current = window.setTimeout(flush, 500)
  }

  // Made can't pass tried: a make adds an attempt, and attempts can't drop below the makes.
  const setMade = (made: Field, tried: Field, n: number) => {
    const t = values[tried]
    change(t !== null && n > t ? { [made]: n, [tried]: n } : { [made]: n })
  }

  const points = (values.fieldGoalsMade ?? 0) * 2 + (values.threePointersMade ?? 0) * 3 + (values.freeThrowsMade ?? 0)

  return (
    <div className="ne">
      <section className="ne-grp" aria-label="Shooting">
        <div className="eg-label">Shooting · made of tried</div>
        {SHOTS.map((s) => (
          <div key={s.made} className="ne-row">
            <span className="ne-name">{s.label}</span>
            <Stepper value={values[s.made] ?? 0} noun={`${s.noun} made`} onChange={(n) => setMade(s.made, s.tried, n)} />
            <span className="ne-of">of</span>
            {values[s.tried] === null
              ? <button className="ne-add" onClick={() => change({ [s.tried]: values[s.made] ?? 0 })}>Add tries</button>
              : <Stepper value={values[s.tried]!} noun={`${s.noun} tried`} min={values[s.made] ?? 0} onChange={(n) => change({ [s.tried]: n })} />}
          </div>
        ))}
        <div className="ne-total"><span>Points</span><b>{points}</b></div>
      </section>

      <section className="ne-grp" aria-label="The rest">
        <div className="eg-label">The rest</div>
        <div className="ne-row">
          <span className="ne-name">Rebounds<small>off · def</small></span>
          {values.offensiveRebounds === null && values.defensiveRebounds === null
            ? <button className="ne-add" onClick={() => change({ offensiveRebounds: 0, defensiveRebounds: 0 })}>Add</button>
            : (
              <>
                <Stepper value={values.offensiveRebounds ?? 0} noun="offensive rebound" onChange={(n) => change({ offensiveRebounds: n })} />
                <Stepper value={values.defensiveRebounds ?? 0} noun="defensive rebound" onChange={(n) => change({ defensiveRebounds: n })} />
              </>
            )}
        </div>
        {REST.map((r) => (
          <div key={r.field} className="ne-row">
            <span className="ne-name">{r.label}</span>
            {values[r.field] === null
              ? <button className="ne-add" onClick={() => change({ [r.field]: 0 })}>Add</button>
              : <Stepper value={values[r.field]!} noun={r.noun} max={r.max} onChange={(n) => change({ [r.field]: n })} />}
          </div>
        ))}
      </section>
    </div>
  )
}

function Stepper({ value, noun, min = 0, max = 99, onChange }: { value: number; noun: string; min?: number; max?: number; onChange: (n: number) => void }) {
  return (
    <span className="ne-step">
      <button onClick={() => onChange(Math.max(min, value - 1))} disabled={value <= min} aria-label={`One ${noun} less`}>−</button>
      <output aria-live="polite">{value}</output>
      <button onClick={() => onChange(Math.min(max, value + 1))} disabled={value >= max} aria-label={`One ${noun} more`}>+</button>
    </span>
  )
}
