import { useState } from 'react'

// Remembers (in this browser) that a banner was closed, so it stays closed.
export function useDismissed(storageKey: string): [boolean, () => void] {
  const [dismissed, setDismissed] = useState(() => {
    try { return localStorage.getItem(storageKey) === '1' } catch { return false }
  })
  const dismiss = () => {
    try { localStorage.setItem(storageKey, '1') } catch { /* private mode - closed for this visit only */ }
    setDismissed(true)
  }
  return [dismissed, dismiss]
}
