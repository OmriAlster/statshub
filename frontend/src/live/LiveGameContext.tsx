import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'

interface LiveGameContextValue {
  overlayOpen: boolean
  openOverlay: () => void
  closeOverlay: () => void
  // Bumped by the live widget when a live game actually starts or finishes
  // (its status and score change). Pages that show that game - the Stats
  // tables, the Dashboard's last/next game, the game page - reload their
  // games when it changes, since they're mounted underneath the widget and
  // otherwise wouldn't know.
  liveGameVersion: number
  markLiveGameChanged: () => void
}

const LiveGameContext = createContext<LiveGameContextValue | null>(null)

export function LiveGameProvider({ children }: { children: ReactNode }) {
  const [overlayOpen, setOverlayOpen] = useState(false)
  const [liveGameVersion, setLiveGameVersion] = useState(0)
  const markLiveGameChanged = useCallback(() => setLiveGameVersion((v) => v + 1), [])

  return (
    <LiveGameContext.Provider
      value={{
        overlayOpen,
        openOverlay: () => setOverlayOpen(true),
        closeOverlay: () => setOverlayOpen(false),
        liveGameVersion,
        markLiveGameChanged,
      }}
    >
      {children}
    </LiveGameContext.Provider>
  )
}

export function useLiveGameOverlay() {
  const ctx = useContext(LiveGameContext)
  if (!ctx) throw new Error('useLiveGameOverlay must be used within a LiveGameProvider')
  return ctx
}
