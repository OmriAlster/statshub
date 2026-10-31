import { useState } from 'react'

export type GamesView = 'cards' | 'table'
const KEY = 'statshub:games-view'

// On a phone the games show as cards, or - by choice - as the table the wider
// screens get. Remembered in this browser; wider screens always show the table.
export function useGamesView(): [GamesView, (view: GamesView) => void] {
  const [view, setView] = useState<GamesView>(() => {
    try { return localStorage.getItem(KEY) === 'table' ? 'table' : 'cards' } catch { return 'cards' }
  })
  const choose = (next: GamesView) => {
    try { localStorage.setItem(KEY, next) } catch { /* private mode - for this visit only */ }
    setView(next)
  }
  return [view, choose]
}
