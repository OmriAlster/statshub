import { useState } from 'react'
import { useInstallPrompt } from '../hooks/useInstallPrompt'

const DISMISSED_KEY = 'statshub_install_banner_dismissed'

export default function InstallAppBanner() {
  const { isStandalone, isIOS, canPromptInstall, promptInstall } = useInstallPrompt()
  const [dismissed, setDismissed] = useState(() => localStorage.getItem(DISMISSED_KEY) === '1')

  const dismiss = () => {
    localStorage.setItem(DISMISSED_KEY, '1')
    setDismissed(true)
  }

  if (isStandalone || dismissed) return null
  if (!canPromptInstall && !isIOS) return null // desktop browser with nothing to offer

  return (
    <div className="install-banner">
      <span className="install-banner-icon">📲</span>
      <div className="install-banner-text">
        {canPromptInstall ? (
          <>
            <strong>Install StatsHub</strong>
            <span>Add it to your home screen for one-tap access, no browser bar.</span>
          </>
        ) : (
          <>
            <strong>Install StatsHub</strong>
            <span>Tap <svg className="icon install-banner-share-icon"><use href="#i-share" /></svg> Share, then "Add to Home Screen".</span>
          </>
        )}
      </div>
      {canPromptInstall && (
        <button className="install-banner-btn" onClick={promptInstall}>Install</button>
      )}
      <button className="install-banner-close" onClick={dismiss} aria-label="Dismiss">
        <svg className="icon"><use href="#i-x" /></svg>
      </button>
    </div>
  )
}
