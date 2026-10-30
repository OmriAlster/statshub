import { useEffect, useRef } from 'react'
import { useGuide } from './GuideContext'

// The first thing a new parent sees: what StatsHub does, and three steps.
export default function WelcomeSheet() {
  const guide = useGuide()
  const startRef = useRef<HTMLButtonElement>(null)
  useEffect(() => startRef.current?.focus(), [])

  return (
    <div className="guide-welcome-backdrop" onClick={guide.closeWelcome}>
      <div className="guide-welcome" role="dialog" aria-modal="true" aria-labelledby="guide-welcome-title" onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => { if (e.key === 'Escape') guide.closeWelcome() }}>
        <h2 id="guide-welcome-title">Welcome to StatsHub</h2>
        <p>Your child's games, stats and league table - in one place. Three quick steps:</p>
        <ol className="guide-steps">
          <li><span>1</span>Add your player</li>
          <li><span>2</span>Connect to IBBA - it happens as you add them</li>
          <li><span>3</span>Track a game live</li>
        </ol>
        <button ref={startRef} type="button" className="guide-start" onClick={guide.startTour}>Let's start</button>
        <button type="button" className="guide-later" onClick={guide.closeWelcome}>Skip for now</button>
      </div>
    </div>
  )
}
