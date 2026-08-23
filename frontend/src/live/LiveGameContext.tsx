import { createContext, useContext, useState, type ReactNode } from 'react'

// A game already scheduled ahead of time (via the Schedule tab), about to be
// promoted to a live in-progress game instead of starting a brand new one
// from scratch.
export interface PendingLiveGame {
  gameId: number
  playerId: number
}

interface LiveGameContextValue {
  overlayOpen: boolean
  openOverlay: () => void
  closeOverlay: () => void
  pendingGame: PendingLiveGame | null
  goLive: (game: PendingLiveGame) => void
  clearPendingGame: () => void
}

const LiveGameContext = createContext<LiveGameContextValue | null>(null)

export function LiveGameProvider({ children }: { children: ReactNode }) {
  const [overlayOpen, setOverlayOpen] = useState(false)
  const [pendingGame, setPendingGame] = useState<PendingLiveGame | null>(null)

  return (
    <LiveGameContext.Provider
      value={{
        overlayOpen,
        openOverlay: () => setOverlayOpen(true),
        closeOverlay: () => setOverlayOpen(false),
        pendingGame,
        goLive: (game) => {
          setPendingGame(game)
          setOverlayOpen(true)
        },
        clearPendingGame: () => setPendingGame(null),
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
