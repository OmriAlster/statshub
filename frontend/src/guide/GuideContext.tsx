import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'
import type { TourStep } from './tour'

// The first-run guide: a welcome with three steps, a short tour that points
// at the real buttons (find the player on IBBA -> add them -> it syncs), and
// one-time tips on other screens. What's been seen is remembered per account
// in this browser - a new phone may show a tip once more, nothing worse.


interface GuideState {
  enabled: boolean
  welcomeOpen: boolean
  tourStep: TourStep | null
  // The welcome for someone who hasn't seen it (no players yet).
  offerWelcome: () => void
  // "?" in the menu - the welcome again, whatever was seen before.
  replay: () => void
  closeWelcome: () => void
  startTour: () => void
  setTourStep: (step: TourStep) => void
  endTour: () => void
  seen: (key: string) => boolean
  markSeen: (key: string) => void
}

const GuideContext = createContext<GuideState | null>(null)

export function GuideProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  // Players (the child's own login) don't add players or track games.
  const enabled = !!user && user.role !== 'Player'
  const prefix = `statshub:guide:${user?.id ?? 'anon'}:`
  const [welcomeOpen, setWelcomeOpen] = useState(false)
  const [tourStep, setTourStepState] = useState<TourStep | null>(null)
  const [version, setVersion] = useState(0) // re-reads storage after a change

  const seen = useCallback((key: string) => {
    void version
    try { return localStorage.getItem(prefix + key) === '1' } catch { return false }
  }, [prefix, version])

  const markSeen = useCallback((key: string) => {
    try { localStorage.setItem(prefix + key, '1') } catch { /* private mode - this visit only */ }
    setVersion((v) => v + 1)
  }, [prefix])

  const value = useMemo<GuideState>(() => ({
    enabled,
    welcomeOpen,
    tourStep,
    offerWelcome: () => { if (enabled && !seen('welcome')) setWelcomeOpen(true) },
    replay: () => {
      try {
        Object.keys(localStorage).filter((k) => k.startsWith(prefix)).forEach((k) => localStorage.removeItem(k))
      } catch { /* nothing stored */ }
      setVersion((v) => v + 1)
      setTourStepState(null)
      setWelcomeOpen(true)
    },
    closeWelcome: () => { markSeen('welcome'); setWelcomeOpen(false) },
    startTour: () => { markSeen('welcome'); setWelcomeOpen(false); setTourStepState('find') },
    setTourStep: (step) => setTourStepState(step),
    endTour: () => { markSeen('tour'); setTourStepState(null) },
    seen,
    markSeen,
  }), [enabled, welcomeOpen, tourStep, seen, markSeen, prefix])

  return <GuideContext.Provider value={value}>{children}</GuideContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useGuide(): GuideState {
  const guide = useContext(GuideContext)
  if (!guide) throw new Error('useGuide outside GuideProvider')
  return guide
}
