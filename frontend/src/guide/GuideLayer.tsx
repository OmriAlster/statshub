import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import CoachMark, { useGuideTarget } from './CoachMark'
import { useGuide } from './GuideContext'
import { TOUR, type TourStep } from './tour'
import WelcomeSheet from './WelcomeSheet'

// What each tour step points at, and what it says.
const STEPS: Record<TourStep, { target: string; last?: boolean; title: string; body: string; done?: string }> = {
  find: { target: 'find-on-ibba', title: 'Find your child on IBBA', body: 'Search by name - no link needed. Tap the highlighted button.' },
  search: { target: 'player-finder', title: 'Type their name in Hebrew', body: "Matches show as you type, with their team's crest. Tap your child." },
  confirm: { target: 'found-confirm', title: "Is it them? Add them", body: 'Team, league and birth date come straight from IBBA - and adding also connects them.' },
  synced: { target: 'ibba-sync', last: true, title: 'That\'s it - it all syncs by itself', body: 'Games every night, results soon after each game. Tap Sync Now any time.', done: 'Got it' },
}

// One-time tips on other screens, first match wins - one at a time.
const TIPS = [
  { key: 'tip-type-filter', path: '/stats', target: 'type-filter', title: 'League, Cup or Friendly', body: 'See the numbers for just those games. The Season tab has the shot chart.' },
  { key: 'tip-live', path: '/', target: 'start-live', title: 'At the game? Track it live', body: 'Tap what happens - points, rebounds, assists. The other parent sees it live.' },
  { key: 'tip-family', path: '/players', target: 'family-actions', title: 'Bring the family', body: 'Invite the other parent, share stats, or give your child their own login.' },
]

const present = (target: string) => !!document.querySelector(`[data-guide="${target}"]`)

export default function GuideLayer() {
  const guide = useGuide()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const { tourStep, setTourStep, endTour } = guide
  const onPlayers = pathname.startsWith('/players')

  // The tour runs on the Players page.
  useEffect(() => {
    if (tourStep && !onPlayers) navigate('/players')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tourStep])

  // Steps move on with what the parent does: tapping the highlighted button,
  // or the next thing showing up (a found player's card, the added player).
  useEffect(() => {
    if (!tourStep) return
    const onClick = (e: MouseEvent) => {
      const el = e.target instanceof Element ? e.target : null
      if (!el) return
      if (el.closest('[data-guide="add-manually"]')) endTour() // not on IBBA - nothing to show
      else if (tourStep === 'find' && el.closest('[data-guide="find-on-ibba"]')) setTourStep('search')
      else if (tourStep === 'confirm' && el.closest('[data-guide="found-confirm"]')) setTourStep('synced')
    }
    const timer = window.setInterval(() => {
      if (tourStep === 'find' && present('player-finder')) setTourStep('search')
      else if (tourStep === 'search' && present('found-confirm')) setTourStep('confirm')
      else if (tourStep === 'confirm' && !present('found-confirm') && present('player-finder')) setTourStep('search') // went back
    }, 300)
    document.addEventListener('click', onClick, true)
    return () => {
      document.removeEventListener('click', onClick, true)
      window.clearInterval(timer)
    }
  }, [tourStep, setTourStep, endTour])

  // A tip's button can show up after the page loads (the Start Live Game
  // button, the type filter) - look again every so often.
  const [, recheck] = useState(0)
  const idle = guide.enabled && !tourStep && !guide.welcomeOpen
  useEffect(() => {
    if (!idle) return
    const timer = window.setInterval(() => recheck((n) => n + 1), 700)
    return () => window.clearInterval(timer)
  }, [idle])

  // Right after the tour (or the welcome) ends, no tip on that same screen -
  // the next one comes on the next screen.
  const [quietOn, setQuietOn] = useState<string | null>(null)
  const wasBusy = useRef(false)
  const busy = !!tourStep || guide.welcomeOpen
  useEffect(() => {
    if (wasBusy.current && !busy) setQuietOn(pathname)
    wasBusy.current = busy
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [busy])

  const step = tourStep ? STEPS[tourStep] : null
  const tip = !step && !guide.welcomeOpen && guide.enabled && quietOn !== pathname
    ? TIPS.find((t) => !guide.seen(t.key) && (t.path === '/' ? pathname === '/' : pathname.startsWith(t.path)) && present(t.target))
    : undefined
  const box = useGuideTarget(step ? step.target : tip ? tip.target : null, step?.last)

  if (!guide.enabled) return null
  if (guide.welcomeOpen) return <WelcomeSheet />

  if (step && tourStep && box && onPlayers) {
    return (
      <CoachMark
        box={box}
        title={step.title}
        body={step.body}
        counter={`${TOUR.indexOf(tourStep) + 1} / ${TOUR.length}`}
        action={step.done ? { label: step.done, onClick: endTour } : undefined}
        onSkip={endTour}
      />
    )
  }

  if (tip && box) {
    return (
      <CoachMark
        box={box}
        title={tip.title}
        body={tip.body}
        counter="Tip"
        action={{ label: 'Got it', onClick: () => guide.markSeen(tip.key) }}
        onSkip={() => { TIPS.forEach((t) => guide.markSeen(t.key)) }}
        skipLabel="No more tips"
      />
    )
  }

  return null
}
