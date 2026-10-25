import { useAuth } from '../auth/AuthContext'
import { usePushNotifications } from '../hooks/usePushNotifications'
import { Busy } from './BouncingBall'
import DismissibleBanner from './DismissibleBanner'
import { useDismissed } from '../hooks/useDismissed'

export default function NotificationBanner() {
  const { user } = useAuth()
  const { supported, permission, subscribed, busy, subscribe } = usePushNotifications(user?.id)
  const [dismissed, dismiss] = useDismissed('statshub_notification_banner_dismissed')

  if (!supported || permission !== 'default' || subscribed || dismissed) return null

  const enable = async () => {
    const granted = await subscribe()
    // Denied permission flips `permission` to 'denied', which already hides
    // this banner on its own - only need to persist the dismissal on success.
    if (granted) dismiss()
  }

  return (
    <DismissibleBanner
      icon="🔔"
      title="Get game notifications"
      onDismiss={dismiss}
      action={
        <button className="install-banner-btn" onClick={enable} disabled={busy}>
          {busy ? <Busy>Enabling…</Busy> : 'Enable'}
        </button>
      }
    >
      New games, live starts, and final scores - straight to your phone.
    </DismissibleBanner>
  )
}
