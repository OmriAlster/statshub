import { Link } from 'react-router-dom'
import { useGuide } from './GuideContext'

// The first steps on the dashboard, ticked off from what's really there -
// gone once all are done, or when closed.
export default function GettingStarted({ hasPlayer, hasIbba, hasTrackedGame, hasFamily = false }: { hasPlayer: boolean; hasIbba: boolean; hasTrackedGame: boolean; hasFamily?: boolean }) {
  const guide = useGuide()
  const ibbaDone = hasIbba || guide.seen('skip-ibba')
  const familyDone = hasFamily || guide.seen('skip-family')
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
    {
      done: familyDone,
      label: 'Bring the family',
      hint: (
        <>
          <Link to="/players?invite=1">Send an invite</Link>
          {' · '}
          <button type="button" className="getting-started-link" onClick={() => guide.markSeen('skip-family')}>Just me</button>
        </>
      ),
    },
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
