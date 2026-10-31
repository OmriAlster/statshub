import { Link } from 'react-router-dom'
import { useGuide } from './GuideContext'

// The three first steps on the dashboard, ticked off from what's really
// there - gone once all three are done, or when closed.
export default function GettingStarted({ hasPlayer, hasIbba, hasTrackedGame }: { hasPlayer: boolean; hasIbba: boolean; hasTrackedGame: boolean }) {
  const guide = useGuide()
  const ibbaDone = hasIbba || guide.seen('skip-ibba')
  const steps = [
    { done: hasPlayer, label: 'Add your player', hint: <Link to="/players">Go to Players</Link> },
    {
      done: ibbaDone,
      label: 'Connect to IBBA',
      hint: (
        <>
          <Link to="/players">Find them on IBBA</Link>
          {' · '}
          <button type="button" className="getting-started-link" onClick={() => guide.markSeen('skip-ibba')}>Not on IBBA</button>
        </>
      ),
    },
    { done: hasTrackedGame, label: 'Track a game live', hint: <span>Tap Start Live Game at the next game</span> },
  ]
  const doneCount = steps.filter((s) => s.done).length
  if (!guide.enabled || guide.seen('checklist-closed') || doneCount === steps.length) return null

  return (
    <section className="getting-started" data-guide="checklist" aria-labelledby="getting-started-title">
      <div className="getting-started-head">
        <h3 id="getting-started-title">Getting started <span>· {doneCount} of {steps.length}</span></h3>
        <button type="button" className="getting-started-close" aria-label="Hide getting started" onClick={() => guide.markSeen('checklist-closed')}>
          <svg className="icon" aria-hidden="true"><use href="#i-x" /></svg>
        </button>
      </div>
      <ol>
        {steps.map((s, i) => (
          <li key={s.label} className={s.done ? 'done' : ''}>
            <span className="getting-started-mark" aria-hidden="true">{s.done ? '✓' : i + 1}</span>
            <span className="getting-started-text">
              <span>{s.label}</span>
              {!s.done && <span className="getting-started-hint">{s.hint}</span>}
            </span>
          </li>
        ))}
      </ol>
    </section>
  )
}
