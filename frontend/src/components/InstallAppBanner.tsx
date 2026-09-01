import { useState } from 'react'
import { useInstallPrompt } from '../hooks/useInstallPrompt'

// One button, not a banner: tap it and the app handles everything it can.
// On Android/Chrome that's the real native install dialog, triggered
// directly. On iOS there's no such API - Apple never built one - so the
// button instead reveals the two-step manual instructions in place.
export default function InstallAppButton() {
  const { isStandalone, isIOS, canPromptInstall, promptInstall } = useInstallPrompt()
  const [showIOSHint, setShowIOSHint] = useState(false)

  if (isStandalone) return null
  if (!canPromptInstall && !isIOS) return null

  const handleClick = () => {
    if (canPromptInstall) {
      promptInstall()
    } else {
      setShowIOSHint((v) => !v)
    }
  }

  return (
    <div className="install-btn-wrap">
      <button className="nav-btn install-btn" onClick={handleClick}>
        <svg className="icon"><use href="#i-install" /></svg>
        <span>Install App</span>
      </button>
      {showIOSHint && (
        <div className="install-hint">
          <span>
            Tap <svg className="icon"><use href="#i-share" /></svg> <b>Share</b>, then <b>"Add to Home Screen"</b>
          </span>
          <button className="install-hint-close" onClick={() => setShowIOSHint(false)} aria-label="Close">
            <svg className="icon"><use href="#i-x" /></svg>
          </button>
        </div>
      )}
    </div>
  )
}
