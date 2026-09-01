import { createContext, useContext, useState, type ReactNode } from 'react'

interface LiveGameContextValue {
  overlayOpen: boolean
  openOverlay: () => void
  closeOverlay: () => void
}

const LiveGameContext = createContext<LiveGameContextValue | null>(null)

export function LiveGameProvider({ children }: { children: ReactNode }) {
  const [overlayOpen, setOverlayOpen] = useState(false)

  return (
    <LiveGameContext.Provider
      value={{
        overlayOpen,
        openOverlay: () => setOverlayOpen(true),
        closeOverlay: () => setOverlayOpen(false),
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
