import { useInstallPrompt } from '../hooks/useInstallPrompt'
import DismissibleBanner from './DismissibleBanner'
import { useDismissed } from '../hooks/useDismissed'

export default function InstallAppBanner() {
  const { isStandalone, isIOS, canPromptInstall, promptInstall } = useInstallPrompt()
  const [dismissed, dismiss] = useDismissed('statshub_install_banner_dismissed')

  if (isStandalone || dismissed) return null
  if (!canPromptInstall && !isIOS) return null // desktop browser with nothing to offer

  return (
    <DismissibleBanner
      icon="📲"
      title="Install StatsHub"
      onDismiss={dismiss}
      action={canPromptInstall && <button className="install-banner-btn" onClick={promptInstall}>Install</button>}
    >
      {canPromptInstall
        ? 'Add it to your home screen for one-tap access, no browser bar.'
        : <>Tap <svg className="icon install-banner-share-icon"><use href="#i-share" /></svg> Share, then "Add to Home Screen".</>}
    </DismissibleBanner>
  )
}
