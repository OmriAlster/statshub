import { useEffect, useState } from 'react'

// Chrome/Edge/Samsung Internet fire this instead of showing their own mini
// install UI when the site is eligible - capturing it lets the app trigger
// the native install dialog from its own button instead of leaving people to
// find it in a browser menu. Safari (iOS and macOS) never fires this event -
// there is no equivalent API there, by Apple's own design.
interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

function isStandaloneNow() {
  return (
    window.matchMedia?.('(display-mode: standalone)').matches ||
    (window.navigator as Navigator & { standalone?: boolean }).standalone === true
  )
}

export function useInstallPrompt() {
  const [deferredEvent, setDeferredEvent] = useState<BeforeInstallPromptEvent | null>(null)
  const [isStandalone, setIsStandalone] = useState(isStandaloneNow)

  useEffect(() => {
    const onBeforeInstallPrompt = (e: Event) => {
      e.preventDefault()
      setDeferredEvent(e as BeforeInstallPromptEvent)
    }
    const onInstalled = () => {
      setDeferredEvent(null)
      setIsStandalone(true)
    }

    window.addEventListener('beforeinstallprompt', onBeforeInstallPrompt)
    window.addEventListener('appinstalled', onInstalled)
    return () => {
      window.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt)
      window.removeEventListener('appinstalled', onInstalled)
    }
  }, [])

  const isIOS = /iphone|ipad|ipod/i.test(window.navigator.userAgent)

  const promptInstall = async () => {
    if (!deferredEvent) return
    await deferredEvent.prompt()
    await deferredEvent.userChoice
    // The captured event is single-use regardless of the user's choice.
    setDeferredEvent(null)
  }

  return {
    isStandalone,
    isIOS,
    canPromptInstall: deferredEvent !== null,
    promptInstall,
  }
}
